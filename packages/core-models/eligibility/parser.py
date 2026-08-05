"""
Infix clinical DSL parser for eligibility criteria.

This module provides a safe, deterministic lexer (tokenizer) and recursive descent
parser to compile human-readable inclusion/exclusion criteria expressions into the
normalized ExpressionNode AST at authoring/design time. Avoids any eval/exec.
"""

import re

from eligibility.models import ExpressionNode, FieldReference

# Token specification
TOKEN_SPEC = [
    ("LPAREN", r"\("),
    ("RPAREN", r"\)"),
    ("COMP_OP", r"==|!=|<=|>=|<|>"),
    ("FIELD_REF", r"eCRF\.[a-zA-Z0-9_]+\.[a-zA-Z0-9_]+"),
    ("BOOL", r"\b(?:True|False|true|false)\b"),
    ("NULL", r"\b(?:None|null|NULL)\b"),
    ("NUMBER", r"-?\d+(?:\.\d+)?\b"),
    ("STRING", r'"[^"\\]*(?:\\.[^"\\]*)*"|\'[^\'\\]*(?:\\.[^\'\\]*)*\''),
    ("LOGICAL", r"\b(?:and|or|not|AND|OR|NOT)\b"),
    ("WS", r"\s+"),
    ("MISMATCH", r"."),  # Catch any unsupported/unmatched character
]


class Token:
    """
    Represents a lexical token identified in the clinical DSL string.
    """

    def __init__(self, type_: str, value: str, position: int):
        self.type = type_
        self.value = value
        self.position = position

    def __repr__(self) -> str:
        return f"Token({self.type}, {self.value!r}, position={self.position})"


def tokenize(code: str) -> list[Token]:
    """
    Splits the clinical DSL code string into a list of Tokens.
    Raises ValueError on syntax error/unsupported character.
    """
    tokens = []
    regex_parts = []
    for name, pattern in TOKEN_SPEC:
        regex_parts.append(f"(?P<{name}>{pattern})")
    master_regex = re.compile("|".join(regex_parts))

    for mo in master_regex.finditer(code):
        kind = mo.lastgroup
        if kind is None:
            continue
        value = mo.group(kind)
        pos = mo.start()

        if kind == "WS":
            continue
        if kind == "MISMATCH":
            raise ValueError(
                f"Syntax error: unexpected character {value!r} at position {pos}"
            )
        tokens.append(Token(kind, value, pos))

    return tokens


class DSLParser:
    """
    Recursive descent parser for compiling clinical DSL tokens into an AST (ExpressionNode).
    """

    def __init__(self, tokens: list[Token], raw_source: str):
        self.tokens = tokens
        self.raw_source = raw_source
        self.pos = 0

    def current_token(self) -> Token | None:
        """
        RETURN the token at the current parsing pointer, or None if EOF.
        """
        if self.pos < len(self.tokens):
            return self.tokens[self.pos]
        return None

    def consume(self, expected_type: str = None, expected_value: str = None) -> Token:
        """
        Consume and return the current token. Raises ValueError if expected_type
        or expected_value do not match.
        """
        tok = self.current_token()
        if not tok:
            raise ValueError("Unexpected end of input.")
        if expected_type and tok.type != expected_type:
            raise ValueError(
                f"Expected token type {expected_type}, got {tok.type} at position {tok.position}."
            )
        if expected_value and tok.value != expected_value:
            raise ValueError(
                f"Expected token value {expected_value!r}, got {tok.value!r} at position {tok.position}."
            )
        self.pos += 1
        return tok

    def parse(self) -> ExpressionNode:
        """
        Run the parser and ensure all tokens have been consumed.
        """
        node = self.parse_expression()
        remaining = self.current_token()
        if remaining is not None:
            raise ValueError(
                f"Unexpected leftover token {remaining.value!r} at position {remaining.position}."
            )
        return node

    def parse_expression(self) -> ExpressionNode:
        """
        Expression -> LogicalOr
        """
        return self.parse_or()

    def parse_or(self) -> ExpressionNode:
        """
        LogicalOr -> LogicalAnd ( "or" LogicalAnd )*
        """
        node = self.parse_and()
        while True:
            tok = self.current_token()
            if tok and tok.type == "LOGICAL" and tok.value.lower() == "or":
                self.consume()
                right = self.parse_and()
                node = ExpressionNode(
                    type="logical", operator="or", operands=[node, right]
                )
            else:
                break
        return node

    def parse_and(self) -> ExpressionNode:
        """
        LogicalAnd -> LogicalNot ( "and" LogicalNot )*
        """
        node = self.parse_not()
        while True:
            tok = self.current_token()
            if tok and tok.type == "LOGICAL" and tok.value.lower() == "and":
                self.consume()
                right = self.parse_not()
                node = ExpressionNode(
                    type="logical", operator="and", operands=[node, right]
                )
            else:
                break
        return node

    def parse_not(self) -> ExpressionNode:
        """
        LogicalNot -> "not" LogicalNot | Comparison
        """
        tok = self.current_token()
        if tok and tok.type == "LOGICAL" and tok.value.lower() == "not":
            self.consume()
            operand = self.parse_not()
            return ExpressionNode(type="logical", operator="not", operands=[operand])
        return self.parse_comparison()

    def parse_comparison(self) -> ExpressionNode:
        """
        Comparison -> Primary [ ComparisonOp Primary ]
        """
        left = self.parse_primary()
        tok = self.current_token()
        if tok and tok.type == "COMP_OP":
            op_tok = self.consume()
            right = self.parse_primary()
            return ExpressionNode(
                type="comparison", operator=op_tok.value, operands=[left, right]
            )
        return left

    def parse_primary(self) -> ExpressionNode:
        """
        Primary -> FIELD_REF | LITERAL | "(" Expression ")"
        """
        tok = self.current_token()
        if not tok:
            raise ValueError("Unexpected end of input, expected primary expression.")

        if tok.type == "LPAREN":
            self.consume()
            node = self.parse_expression()
            self.consume("RPAREN")
            return node

        if tok.type == "FIELD_REF":
            self.consume()
            parts = tok.value.split(".")
            field_ref_obj = FieldReference(
                raw_reference=tok.value, domain=parts[1], variable=parts[2]
            )
            return ExpressionNode(type="field_ref", field_ref=field_ref_obj)

        if tok.type == "BOOL":
            self.consume()
            val = tok.value.lower() == "true"
            return ExpressionNode(type="constant", value=val)

        if tok.type == "NULL":
            self.consume()
            return ExpressionNode(type="constant", value=None)

        if tok.type == "NUMBER":
            self.consume()
            val = float(tok.value) if "." in tok.value else int(tok.value)
            return ExpressionNode(type="constant", value=val)

        if tok.type == "STRING":
            self.consume()
            # Strip outer single or double quotes
            val = tok.value[1:-1]
            # Replace escaped quotes
            val = val.replace('\\"', '"').replace("\\'", "'")
            return ExpressionNode(type="constant", value=val)

        raise ValueError(
            f"Unexpected token {tok.value!r} of type {tok.type} at position {tok.position}."
        )


def parse_dsl(dsl_source: str) -> ExpressionNode:
    """
    Parses raw clinical DSL source string and returns compiled Pydantic ExpressionNode AST.
    Raises ValueError on syntax error or malformed input.
    """
    if not dsl_source or not dsl_source.strip():
        raise ValueError("DSL source string cannot be empty or whitespace.")

    tokens = tokenize(dsl_source)
    if not tokens:
        raise ValueError(f"No valid tokens found in DSL source: '{dsl_source}'.")

    parser = DSLParser(tokens, dsl_source)
    return parser.parse()
