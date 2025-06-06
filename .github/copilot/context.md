# Project Context for GitHub Copilot

## Project Structure
- `ui/src/app`: Angular components and services
- `api/ExamApp.Api`: .NET Core backend API
- `api/ExamApp.Api/Controllers`: API endpoints
- `api/ExamApp.Api/Services`: Business logic

## Data Models
- Test: Represents an exam with questions
- Subject: Academic subjects like Math, Science
- Topic/Subtopic: Hierarchical categorization of subjects
- Grade: School grade levels
- Book/BookTest: Reference materials

## Key Workflows
1. Test creation (single or bulk from Excel)
2. Question management
3. Subject/topic management
4. Grade-specific content filtering