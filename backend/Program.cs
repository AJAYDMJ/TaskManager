using backend.Data;
using backend.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtKey = jwtSection["Key"] ?? throw new InvalidOperationException("Jwt:Key is required.");
var jwtIssuer = jwtSection["Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is required.");
var jwtAudience = jwtSection["Audience"] ?? throw new InvalidOperationException("Jwt:Audience is required.");

builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:5174")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddDbContext<TaskDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();
    db.Database.Migrate();
}

app.MapPost("/api/auth/register", async (RegisterRequest request, TaskDbContext db) =>
{
    var username = request.Username.Trim();
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest("Username and password are required.");
    }

    var existingUser = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (existingUser is not null)
    {
        return Results.BadRequest("Username already exists.");
    }

    var passwordHasher = new PasswordHasher<AppUser>();
    var user = new AppUser { Username = username };
    user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Username });
});

app.MapPost("/api/auth/login", async (LoginRequest request, TaskDbContext db) =>
{
    var username = request.Username.Trim();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var passwordHasher = new PasswordHasher<AppUser>();
    var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
    if (verification == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    var token = CreateToken(user, jwtKey, jwtIssuer, jwtAudience);
    return Results.Ok(new { token });
});

var taskGroup = app.MapGroup("/api/tasks").RequireAuthorization();

taskGroup.MapGet("", async (ClaimsPrincipal principal, TaskDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    var tasks = await db.Tasks
        .Where(t => t.UserId == userId)
        .OrderByDescending(t => t.CreatedAt)
        .ToListAsync();

    return Results.Ok(tasks);
});

taskGroup.MapGet("/{id:int}", async (int id, ClaimsPrincipal principal, TaskDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
    return task is null ? Results.NotFound() : Results.Ok(task);
});

taskGroup.MapPost("", async (TaskItem task, ClaimsPrincipal principal, TaskDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    task.Id = 0;
    task.UserId = userId;
    task.CreatedAt = DateTime.UtcNow;
    db.Tasks.Add(task);
    await db.SaveChangesAsync();
    return Results.Created($"/api/tasks/{task.Id}", task);
});

taskGroup.MapPut("/{id:int}", async (int id, TaskItem updatedTask, ClaimsPrincipal principal, TaskDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    var existingTask = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
    if (existingTask is null)
    {
        return Results.NotFound();
    }

    existingTask.Title = updatedTask.Title;
    existingTask.IsCompleted = updatedTask.IsCompleted;
    await db.SaveChangesAsync();
    return Results.NoContent();
});

taskGroup.MapDelete("/{id:int}", async (int id, ClaimsPrincipal principal, TaskDbContext db) =>
{
    var userId = GetUserId(principal);
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
    if (task is null)
    {
        return Results.NotFound();
    }

    db.Tasks.Remove(task);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

static int? GetUserId(ClaimsPrincipal principal)
{
    var claimValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
    return int.TryParse(claimValue, out var userId) ? userId : null;
}

static string CreateToken(AppUser user, string key, string issuer, string audience)
{
    var claims = new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username)
    };

    var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(8),
        signingCredentials: credentials);

    return new JwtSecurityTokenHandler().WriteToken(token);
}

record RegisterRequest(string Username, string Password);
record LoginRequest(string Username, string Password);
