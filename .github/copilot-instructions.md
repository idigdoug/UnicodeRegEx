# Copilot Instructions

## Project Guidelines
- The UnicodeRegEx CLI (UnicodeRegExCli / unirex) is primarily scaffolding for developing the Tools library; the GUI is the intended primary consumer of Tools, and the CLI is a secondary "bonus".
- The command-line arguments (both short and long forms) should align with GNU grep conventions as closely as reasonably possible (e.g., -r = recurse, -i = ignore-case). GNU grep is used as a design oracle (a vetted feature checklist), not a compatibility target — features valuable enough to exist in grep are candidates for the library, but the project deliberately omits some grep features and exceeds grep in others.