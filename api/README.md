# ExamApp - Online Examination System

A comprehensive online examination platform built with ASP.NET Core and Angular, featuring secure authentication, role-based access control, and real-time exam management.

## 🚀 Features

### Core Functionality
- **Multi-Role System**: Support for Students, Teachers, and Parents
- **Worksheet Management**: Create, manage, and distribute digital worksheets
- **Real-time Examinations**: Start tests, save answers, and track progress
- **Question Bank**: Comprehensive question management with multiple subjects
- **Student Analytics**: Performance tracking and statistical analysis
- **Canvas Integration**: Advanced test canvas with drawing capabilities

### Authentication & Security
- **Keycloak Integration**: Enterprise-grade authentication and authorization
- **JWT Token Management**: Secure token-based authentication with refresh tokens
- **Role-based Access Control**: Fine-grained permissions for different user types
- **Profile Management**: User profile caching and management

### Technical Features
- **File Management**: MinIO integration for secure file storage
- **Redis Caching**: Enhanced performance with distributed caching
- **PostgreSQL Database**: Robust data persistence with Entity Framework Core
- **Docker Support**: Containerized deployment with Docker Compose
- **API Documentation**: Swagger/OpenAPI integration

## 🏗️ Architecture

### Backend (ASP.NET Core 8.0)
```
ExamApp.Api/
├── Controllers/          # API endpoints
├── Services/            # Business logic layer
├── Data/               # Database models and context
├── Models/Dtos/        # Data transfer objects
├── Helpers/            # Utility classes
└── Migrations/         # Database migrations
```

### Key Components
- **Controllers**: RESTful API endpoints for all features
- **Services**: Business logic with dependency injection
- **Data Layer**: Entity Framework Core with PostgreSQL
- **Authentication**: Keycloak integration with JWT

## 🛠️ Technology Stack

### Backend
- **Framework**: ASP.NET Core 8.0
- **Database**: PostgreSQL with Entity Framework Core 9.0
- **Authentication**: Keycloak + JWT Bearer tokens
- **Caching**: Redis (StackExchange.Redis)
- **File Storage**: MinIO
- **Security**: BCrypt.Net for password hashing

### Infrastructure
- **Containerization**: Docker & Docker Compose
- **Web Server**: Kestrel
- **Documentation**: Swagger/OpenAPI
- **Monitoring**: Built-in logging and health checks

## 📋 Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker & Docker Compose](https://docs.docker.com/get-docker/)
- [PostgreSQL](https://www.postgresql.org/) (or use Docker)
- [Redis](https://redis.io/) (or use Docker)
- [Keycloak](https://www.keycloak.org/) (or use Docker)

## 🚀 Quick Start

### 1. Clone the Repository
```bash
git clone <repository-url>
cd ExamApp
```

### 2. Environment Setup
Copy the configuration files and update settings:
```bash
cp ExamApp.Api/appsettings.json ExamApp.Api/appsettings.Development.json
```

Update the connection strings and Keycloak settings in `appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=examapp;Username=postgres;Password=yourpassword"
  },
  "Keycloak": {
    "Authority": "http://localhost:8080/realms/exam-realm",
    "ClientId": "exam-client",
    "ClientSecret": "your-client-secret"
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  }
}
```

### 3. Database Setup
```bash
cd ExamApp.Api
dotnet ef database update
```

### 4. Run with Docker (Recommended)
```bash
docker-compose up -d
```

### 5. Run Locally
```bash
cd ExamApp.Api
dotnet run
```

The API will be available at `https://localhost:5079`

## 📡 API Endpoints

### Authentication
- `POST /api/auth/login` - User login
- `POST /api/auth/register` - User registration
- `POST /api/auth/refresh-token` - Refresh access token
- `POST /api/auth/logout` - User logout

### Worksheets/Exams
- `GET /api/worksheet/list` - Get worksheets with filtering
- `POST /api/worksheet/start-test/{testId}` - Start a test
- `POST /api/worksheet/save-answer` - Save student answer
- `PUT /api/worksheet/end-test/{testInstanceId}` - End test session
- `GET /api/worksheet/student/statistics` - Get student performance stats

### Questions
- `GET /api/questions/{id}` - Get question by ID
- `GET /api/questions/bytest/{testId}` - Get questions for a test
- `POST /api/questions` - Create new question

### Students
- `POST /api/student/update-grade` - Update student grade
- `POST /api/student/update-avatar` - Update student avatar

### Subjects & Books
- `GET /api/subject` - Get all subjects
- `GET /api/subject/topics/{subjectId}` - Get topics by subject
- `GET /api/books` - Get all books
- `GET /api/books/{bookId}/tests` - Get tests by book

## 🏢 Business Logic

### User Roles
1. **Students**: Take exams, view results, track progress
2. **Teachers**: Create worksheets, manage questions, view student analytics
3. **Parents**: Monitor student progress and performance

### Exam Flow
1. **Start Test**: Student initiates a test session
2. **Answer Questions**: Real-time answer saving with progress tracking
3. **Submit Test**: Automatic or manual test completion
4. **Results**: Immediate scoring and detailed analytics

### Question Management
- Multiple choice questions with correct answer tracking
- Image support for questions and answers
- Subject and topic categorization
- Difficulty levels and metadata

## 🔧 Configuration

### Database Connection
Configure PostgreSQL connection in `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=postgres;Port=5432;Database=examapp;Username=postgres;Password=password"
  }
}
```

### Keycloak Setup
```json
{
  "Keycloak": {
    "Authority": "http://keycloak:8080/realms/exam-realm",
    "ClientId": "exam-client",
    "ClientSecret": "your-secret"
  }
}
```

### Redis Configuration
```json
{
  "Redis": {
    "ConnectionString": "redis:6379"
  }
}
```

## 🔍 Development

### Database Migrations
```bash
# Add new migration
dotnet ef migrations add MigrationName

# Update database
dotnet ef database update

# Remove last migration
dotnet ef migrations remove
```

### Running Tests
```bash
cd ExamApp.Tests
dotnet test
```

### Performance Testing
The project includes K6 performance testing scripts:
```bash
k6 run k6-test.js
```

## 📦 Docker Deployment

### Using Docker Compose
```bash
# Start all services
docker-compose up -d

# View logs
docker-compose logs -f

# Stop services
docker-compose down
```

### Individual Container
```bash
# Build image
docker build -t examapp-api .

# Run container
docker run -p 8005:8005 examapp-api
```

## 🧪 Testing

### Load Testing with K6
```bash
# Install K6
npm install -g k6

# Run performance tests
k6 run k6-test.js
```

### Unit Tests
```bash
cd ExamApp.Tests
dotnet test --verbosity normal
```

## 📊 Monitoring & Logging

- **Application Logs**: Built-in .NET logging with configurable levels
- **Health Checks**: Endpoint monitoring for dependencies
- **Performance Metrics**: Request/response timing and throughput
- **Error Tracking**: Comprehensive exception handling and logging

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 📞 Support

For support and questions:
- Create an issue on GitHub
- Contact the development team
- Check the documentation in the `/docs` folder

## 🗺️ Roadmap

- [ ] Mobile app development
- [ ] Advanced analytics dashboard
- [ ] AI-powered question generation
- [ ] Video call integration for proctoring
- [ ] Multi-language support
- [ ] Advanced reporting features

---

**ExamApp** - Transforming education through technology 🎓
