# Coding Conventions

## Angular
- Use standalone components with imports array
- Output events are prefixed with 'on' (e.g., onSubmit)
- Use reactive forms with explicit FormControl typing
- Follow Angular Material design patterns

## C# Backend
- Follow REST API conventions
- Use async/await for database operations
- Services implement interface with 'I' prefix
- Controllers inherit from BaseController
- Use DTOs for data transfer between layers

## Naming Conventions
- Angular components: kebab-case filenames, PascalCase class names
- C# classes: PascalCase
- Variables: camelCase
- Database tables: PascalCase singular (e.g., Subject, not Subjects)