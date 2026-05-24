# Codebase Naming Conventions

## File Generation Constraints
- **Mandatory Prefix:** Any new markdown (`.md`) or text (`.txt`) files you suggest, generate, or create **must** be prefixed with `AI_`.
- **Examples:** Use `AI_api_documentation.md` instead of `api_documentation.md`. Use `AI_scratchpad.txt` instead of `scratchpad.txt`.
- **Reason:** This prevents AI-generated files from cluttering Git tracking and aligns with our local `.gitignore` configurations. Never emit a `.md` or `.txt` filename without this prefix.

## Documentation & Project Context
- **AI generated documentation:** Local project documentation, architectural decisions, and developer notes may be stored in files prefixed with `AI_` (e.g., `AI_architecture.md`, `AI_api_specs.txt`).
- **Context Retrieval:** Before generating code or answering questions about the workspace, scan the repository for any `AI_` prefixed files and use their content as your additional reference guidelines.
