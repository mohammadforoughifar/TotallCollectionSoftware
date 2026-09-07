using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RadisHr.Api.Data;
using RadisHr.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ───── پایگاه داده: SQL Server محلی با احراز هویت ویندوز (Server=.)
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Server=.;Database=RadisHrV019;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

// حالت نمایشی (Demo) فقط برای پیش‌نمایش بدون SQL Server — با Database:Provider=InMemory فعال می‌شود
var provider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var useInMemory = string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (useInMemory) options.UseInMemoryDatabase("RadisHrV019");
    else options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3));
});

// ───── سرویس‌ها
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<PayrollService>();
builder.Services.AddScoped<AttendanceService>();

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "RADIS-HR V019 API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    c.AddSecurityRequirement(new()
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ───── احراز هویت JWT
var jwtKey = builder.Configuration["Jwt:Key"] ?? "RADIS-HR-V019-DOTNET8-DEFAULT-SIGNING-KEY-CHANGE-ME-32B";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "RadisHr",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "RadisHrClient",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// ───── ایجاد/به‌روزرسانی پایگاه داده و درج داده‌های پایه
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        if (useInMemory) db.Database.EnsureCreated();
        else if (db.Database.GetPendingMigrations().Any()) db.Database.Migrate();
        else db.Database.EnsureCreated();
        await DbSeeder.SeedAsync(db, app.Configuration);
        logger.LogInformation("پایگاه داده RadisHrV019 آماده است.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "اتصال به SQL Server برقرار نشد — رشتهٔ اتصال: {cs}", connectionString);
    }
}

app.UseCors();
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "RADIS-HR API v1"));
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// هر نشانی api/* که با کنترلری تطبیق نکند باید خطای روشن JSON بدهد،
// نه اینکه به index.html برگردد (که باعث خطاهای گیج‌کنندهٔ 405/200 می‌شود).
app.Map("/api/{**rest}", (HttpContext ctx) =>
    Results.NotFound(new { ok = false, message = $"سرویس مورد نظر یافت نشد: {ctx.Request.Method} {ctx.Request.Path}" }));

app.MapFallbackToFile("index.html");

app.Run();
