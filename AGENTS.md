# AGENTS.md

## Repository expectations

- Write comments for readers who are new to the codebase but understand the project's goals. Explain why code exists and how it fits into the system, rather than restating what the code does.
- Validate invariants at every boundary where external data enters the system, such as constructors, factory methods, setters, public methods, API endpoints, deserialization, or other input boundaries. Assume nothing about inputs. Invalid data should fail immediately with a clear, actionable error rather than silently corrupting state and causing failures later. Use assertions or equivalent mechanisms for programmer errors that cannot reasonably be recovered from. Use normal error-handling mechanisms for expected runtime failures that callers can recover from.
- Prefer a single, canonical way to construct an object or value so that all invariants are enforced consistently. Keep internal state encapsulated, exposing it only through well-defined interfaces when needed. Avoid multiple construction paths, public mutable state, or builder patterns unless they are justified by genuinely optional or complex configuration. If a builder is used, it should delegate validation to the canonical construction path.
