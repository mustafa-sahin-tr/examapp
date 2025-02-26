using System;
using Xunit;
using Moq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamApp.Api.Controllers;
using ExamApp.Api.Models;
using System.Threading.Tasks;
using System.Linq;
using ExamApp.Api.Services;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Tests;

public class DatabaseFixtureForAuth : IDisposable
{
    public AppDbContext Context { get; private set; }

    public DatabaseFixtureForAuth()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDatabase")
            .Options;

        Context = new AppDbContext(options);

        // Seed the database with initial data
        SeedDatabase();
    }

    private void SeedDatabase()
    {
        Context.Users.Add(new User { Email = "test@example.com", FullName = "Test Test", PasswordHash = BCrypt.Net.BCrypt.HashPassword("password") });
        Context.SaveChanges();
    }

    public void Dispose()
    {
        Context.Dispose();
    }
}

public class AuthControllerTests
{
    private readonly AuthController _controller;
    private readonly AppDbContext _context;
    private readonly Mock<IJwtService> _jwtServiceMock;

    public AuthControllerTests()
    {
        _context = new DatabaseFixtureForAuth().Context;
        _jwtServiceMock = new Mock<IJwtService>();
        _controller = new AuthController(_context, _jwtServiceMock.Object);
    }

    [Fact]
    public async Task Register_ReturnsBadRequest_WhenEmailExists()
    {
        // Arrange
        var request = new RegisterDto { Email = "test@example.com", Password = "password", FullName = "Test User", Role = UserRole.Student };
        // Act
        var result = await _controller.Register(request);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_WhenInvalidCredentials()
    {
        // Arrange
        var request = new LoginDto { Email = "test@example.com", Password = "wrongpassword" };        
        
        // Act
        var result = await _controller.Login(request);

        // Assert
        Assert.IsType<UnauthorizedObjectResult>(result);
    }
}
