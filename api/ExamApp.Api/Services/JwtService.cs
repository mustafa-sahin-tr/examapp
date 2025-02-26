using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ExamApp.Api.Data;
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
                    claims = claims.Append(new Claim("StudentId", user.Student.Id.ToString())).ToArray();
                }
                break;
            case UserRole.Teacher:
                if (user.Teacher != null)
                {
                    claims = claims.Append(new Claim("TeacherId", user.Teacher.Id.ToString())).ToArray();
                }
                break;
            case UserRole.Parent:
                if (user.Parent != null)
                {
                    claims = claims.Append(new Claim("ParentId", user.Parent.Id.ToString())).ToArray();
                }
                break;
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:Key"]));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        var token = new JwtSecurityToken(
            _config["Jwt:Issuer"],
            _config["Jwt:Audience"],
            claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ClaimsPrincipal ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]);

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
