# ExamApp

A comprehensive examination platform with modern architecture and multi-service components.

## Overview

ExamApp is a full-featured examination platform built with a microservices architecture. The system includes:

- Angular 19 frontend
- .NET Core API services
- AI-powered question detection
- Authentication via Keycloak
- Object storage with MinIO
- Message processing with RabbitMQ
- API Gateway (Ocelot)
- PostgreSQL database

## System Architecture

The application consists of the following microservices:

- **Frontend**: Angular 19 UI
- **API Services**:
  - Exam API - Core examination service
  - Badge API - Student achievement badges service
  - Outbox Publisher - Event processing service
- **Gateway**: Ocelot Gateway for API routing
- **Question Detection**: AI-powered question processing engine
- **Storage**: MinIO object storage for files and images
- **Database**: PostgreSQL
- **Message Queue**: RabbitMQ
- **Identity**: Keycloak for authentication and authorization
- **Monitoring**: pgAdmin for database management

## Getting Started

### Prerequisites

- Docker and Docker Compose
- Git

### Running the Application

1. Clone the repository:

```bash
git clone [repository-url]
cd examapp
```

2. Start the application with Docker Compose:

```bash
docker-compose up
```

This command will spin up all the required services. For development use, you can run:

```bash
docker-compose -f docker-compose.yaml -f docker-compose.override.yml up
```

### Accessing Services

Once the application is running, you can access the following services:

- **Frontend**: [http://localhost:5678](http://localhost:5678)

## Development

### Frontend Development

The frontend is built with Angular 19. To start development:

```bash
cd ui
npm install
ng serve
```

### Backend Development

The backend services are .NET Core applications:

```bash
cd api
dotnet run --project ExamApp.Api
```

### Running Tests

For the frontend:

```bash
cd ui
ng test
```

For load testing, K6 is configured:

```bash
docker-compose run k6 run /home/k6/testfiles/k6-script.js
```

## Configuration

Each service has its own configuration in the respective Docker Compose files and application settings.

## Additional Resources

- [Angular Documentation](https://angular.dev)
- [.NET Core Documentation](https://docs.microsoft.com/en-us/dotnet)
- [Keycloak Documentation](https://www.keycloak.org/documentation)
- [MinIO Documentation](https://docs.min.io)
- [RabbitMQ Documentation](https://www.rabbitmq.com/documentation.html)
