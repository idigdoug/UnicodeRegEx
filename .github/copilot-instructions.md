# Copilot Instructions

## Project Guidelines
- The UnicodeRegEx CLI (project UnicodeRegExManagedCli; user-facing exe UniGrep.exe, command "unigrep") is primarily scaffolding for developing the Tools library; the GUI is the intended primary consumer of Tools, and the CLI is a secondary "bonus".
- The command-line arguments (both short and long forms) should align with GNU grep conventions as closely as reasonably possible (e.g., -r = recurse, -i = ignore-case). GNU grep is used as a design oracle (a vetted feature checklist), not a compatibility target — features valuable enough to exist in grep are candidates for the library, but the project deliberately omits some grep features and exceeds grep in others.

## GUI Verification Guidelines
- When verifying the UnicodeRegEx WinForms GUI (project UnicodeRegExManagedGui; user-facing exe UniRex.exe), do NOT launch the app from the agent — it is a blocking WinForms app that stays open until the user closes it manually, which locks the exe and causes 'waiting for file to become accessible' build warnings. Rely on build + tests for verification instead.