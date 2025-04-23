using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Constants;
using ExamApp.Api.Services;
using Microsoft.IdentityModel.Tokens;

public class JwtService : IJwtService
{
    private readonly IConfiguration _config;

    public JwtService(IConfiguration config)
    {
        _config = config;
    }

    public string GenerateToken(User user)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        switch (user.Role)
        {
            case UserRole.Student:
                if (user.Student != null)
                {
                    claims = claims.Append(new Claim(ExamClaimTypes.StudentId, user.Student.Id.ToString())).ToArray();
                }
                break;
            case UserRole.Teacher:
                if (user.Teacher != null)
                {
                    claims = claims.Append(new Claim(ExamClaimTypes.TeacherId, user.Teacher.Id.ToString())).ToArray();
                }
                break;
            case UserRole.Parent:
                if (user.Parent != null)
                {
                    claims = claims.Append(new Claim(ExamClaimTypes.ParentId, user.Parent.Id.ToString())).ToArray();
                }
                break;
        }

        var jwtKey = _config[ConfigConstants.JwtKey] ?? throw new InvalidOperationException("JWT key is not configured");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var token = new JwtSecurityToken(
            _config[ConfigConstants.JwtIssuer],
            _config[ConfigConstants.JwtAudience],      
            claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var jwtKey = _config[ConfigConstants.JwtKey] ?? throw new InvalidOperationException("JWT key is not configured");
        var key = Encoding.UTF8.GetBytes(jwtKey);

        var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        }, out SecurityToken validatedToken);

        return principal;
    }
}
