# Copilot Instructions

## Project Guidelines
- When editing files in the UnicodeRegEx workspace (especially test\RegExTests.cpp), preserve the UTF-8 BOM. Do not use PowerShell ReadAllText/WriteAllText for bulk edits, as they strip the BOM and cause MSVC to misinterpret source encoding.