/**
 * Isomorphic AST Rules Evaluator for Vue 3 Headless Form Renderer.
 * Evaluates declarative AST expressions (JSONLogic compatible) in client memory
 * for sub-millisecond red-text feedback and reactive derivations.
 */

export class ClientRulesEvaluator {
  /**
   * Evaluate an AST expression against a data payload.
   * @param {Object} expr AST expression object (e.g. {">": [{"var": "VS_SYSBP"}, {"var": "VS_DIABP"}]})
   * @param {Object} data Key-value map of form values
   * @returns {*} Evaluated result
   */
  static evaluate(expr, data) {
    if (expr === null || expr === undefined || typeof expr !== 'object') {
      return expr
    }

    // Array evaluation
    if (Array.isArray(expr)) {
      return expr.map(item => this.evaluate(item, data))
    }

    const keys = Object.keys(expr)
    if (keys.length === 0) return expr
    const op = keys[0]
    const args = expr[op]

    switch (op) {
      case 'var': {
        const varName = Array.isArray(args) ? args[0] : args
        const defaultVal = Array.isArray(args) && args.length > 1 ? args[1] : null
        return data && varName in data ? data[varName] : defaultVal
      }
      case '==':
      case '===': {
        const [a, b] = Array.isArray(args) ? args : [args, null]
        return this.evaluate(a, data) === this.evaluate(b, data)
      }
      case '!=':
      case '!==': {
        const [a, b] = Array.isArray(args) ? args : [args, null]
        return this.evaluate(a, data) !== this.evaluate(b, data)
      }
      case '>': {
        const [a, b] = args
        return Number(this.evaluate(a, data)) > Number(this.evaluate(b, data))
      }
      case '>=': {
        const [a, b] = args
        return Number(this.evaluate(a, data)) >= Number(this.evaluate(b, data))
      }
      case '<': {
        const [a, b] = args
        return Number(this.evaluate(a, data)) < Number(this.evaluate(b, data))
      }
      case '<=': {
        const [a, b] = args
        return Number(this.evaluate(a, data)) <= Number(this.evaluate(b, data))
      }
      case '+': {
        const evaluated = args.map(arg => Number(this.evaluate(arg, data)))
        return evaluated.reduce((acc, curr) => acc + curr, 0)
      }
      case '-': {
        const [a, b] = args
        return Number(this.evaluate(a, data)) - Number(this.evaluate(b, data))
      }
      case '*': {
        const [a, b] = args
        return Number(this.evaluate(a, data)) * Number(this.evaluate(b, data))
      }
      case '/': {
        const [a, b] = args
        const denom = Number(this.evaluate(b, data))
        if (denom === 0) return null
        return Number(this.evaluate(a, data)) / denom
      }
      case 'and': {
        return args.every(arg => Boolean(this.evaluate(arg, data)))
      }
      case 'or': {
        return args.some(arg => Boolean(this.evaluate(arg, data)))
      }
      case '!': {
        const target = Array.isArray(args) ? args[0] : args
        return !Boolean(this.evaluate(target, data))
      }
      default:
        return null
    }
  }
}
