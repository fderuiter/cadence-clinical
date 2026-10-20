# ICH E2B(R3) ICSR schema (vendored)

The XML schema of the ICH E2B(R3) Individual Case Safety Report message: the HL7 v3 `MCCI_IN200100UV01` batch
interaction, the `PORR_IN049016UV` message it carries, and the HL7 core schemas they include. The safety service embeds
these files and validates every ICSR it renders against `multicacheschemas/MCCI_IN200100UV01.xsd`
(`apps/safety/src/Infrastructure/Standards/IcsrSchema.cs`, #5573); `tests/Cadence.UnitTests` validates the rendered
fixtures against the same files with an independent `XmlSchemaSet`.

## Source

- Package: ICH E2B(R3) Implementation Guide complete package v1.11.1, `IG_Complete_Package_v1_11_1.zip`, published on
  the ICH page
  [E2B(R3) Individual Case Safety Report (ICSR) Specification and Related Files](https://ich.org/page/e2br3-individual-case-safety-report-icsr-specification-and-related-files)
  (`https://admin.ich.org/sites/default/files/inline-files/IG_Complete_Package_v1_11_1.zip`).
- SHA-256 of that zip: `a3e3f4e2fbec61abec6a3166cb96cc8236f496b08554f71ff661e4ae8172ff72`.
- Files: copied byte for byte from the package's `4_ICH_ICSR_Schema_Files/coreschemas/` and
  `4_ICH_ICSR_Schema_Files/multicacheschemas/` folders. Only the 50 files that `MCCI_IN200100UV01.xsd` reaches through
  its includes are kept; nothing is edited. Do not edit them: replace them from a newer ICH package instead, and update
  this file.

## What the schema does not check

The schema checks the message structure, HL7 data types and HL7 vocabularies. It does not check the ICH business rules
(mandatory elements, the ICH code lists, date precision, conditional elements), which `Domain/IcsrConformance.cs`
checks before rendering, following `11_ICH E2B(R3) Core Data Elements and Business Rules ver.1.00.xlsx` from the same
package. Neither checks MedDRA terms against the dictionary: MedDRA is licensed and is not in the repository, so only the
form of the version (`NN.N`) and of the LLT code (eight digits) is checked.

## Licences

- The HL7 schema files carry the Health Level Seven copyright and its redistribution licence in their header comment,
  kept unchanged. As that licence asks: this product includes software developed by Health Level Seven.
- The ICH E2B(R3) specification and package are copyright of the International Council for Harmonisation of Technical
  Requirements for Pharmaceuticals for Human Use (ICH), used and redistributed under ICH's public licence, which
  requires ICH's copyright to be acknowledged.
