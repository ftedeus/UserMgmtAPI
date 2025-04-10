using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// In-memory user list
var users = new List<User>
{
    new User { Id = 1, Name = "Alice", Email = "alice@example.com" },
    new User { Id = 2, Name = "Bob", Email = "bob@example.com" }
};
var usersLock = new object();

// Use custom exception handling middleware
app.UseExceptionHandlingMiddleware();

// Use HTTPS redirection
app.UseHttpsRedirection();

// Use the logging middleware
app.UseLoggingMiddleware();

// Use authentication middleware
app.UseAuthenticationMiddleware();



// Map endpoints after middleware
app.MapGet("/", () => "Hello, world!");

// GET: Retrieve all users
app.MapGet("/users", () => Results.Ok(users));

// GET: Retrieve a specific user by ID

app.MapGet("/users/{id:int}", (int id) =>
{
    var user = users.FirstOrDefault(u => u.Id == id);
    return user is null ? Results.NotFound() : Results.Ok(user);
});

// thread-safe write operation
app.MapPost("/users", (HttpContext context, User user) =>
{
    // Validate the incoming user
    var validationResults = new List<ValidationResult>();
    var validationContext = new ValidationContext(user, serviceProvider: null, items: null);

    if (!Validator.TryValidateObject(user, validationContext, validationResults, validateAllProperties: true))
    {
        // Return validation errors
        //return Results.BadRequest(validationResults);
         var errors = validationResults.Select(vr => vr.ErrorMessage).ToList();
    return Results.BadRequest(new { Errors = errors });
    }

    // Add the valid user to the list
    
 lock (usersLock)
    {
         user.Id = 0; // Reset the ID to ensure it's generated
        user.Id = users.Count > 0 ? users.Max(u => u.Id) + 1 : 1;
        users.Add(user);
    }
    return Results.Created($"/users/{user.Id}", user);
});




app.MapPut("/users/{id}", (int id, User updatedUser) =>
{
    var user = users.FirstOrDefault(u => u.Id == id);
    if (user is null) return Results.NotFound();

    // Validate the incoming updated user
    var validationResults = new List<ValidationResult>();
    var validationContext = new ValidationContext(updatedUser, serviceProvider: null, items: null);

 

    if (!Validator.TryValidateObject(updatedUser, validationContext, validationResults, validateAllProperties: true))
{
    var errors = validationResults.Select(vr => new { Field = vr.MemberNames.FirstOrDefault(), Error = vr.ErrorMessage }).ToList();
    return Results.BadRequest(new { Errors = errors });
}

    // Update the existing user if validation passes

    lock (usersLock)
{
    user.Name = updatedUser.Name;
    user.Email = updatedUser.Email;
}
    
    return Results.Ok(user);
});


// DELETE: Remove a user by ID
app.MapDelete("/users/{id}", (int id) =>
{
    var user = users.FirstOrDefault(u => u.Id == id);
    if (user is null) return Results.NotFound();

    users.Remove(user);
    return Results.NoContent();
});

// Single app.Run() call
app.Run();

// User class
public class User
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(50, ErrorMessage = "Name cannot exceed 50 characters.")]
    public required string Name { get; set; }

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email address format.")]
    public required string Email { get; set; }
}


// Logging Middleware
public class LoggingMiddleware
{
    private readonly RequestDelegate _next;

    public LoggingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Log the HTTP request
        Console.WriteLine($"Request: {context.Request.Method} {context.Request.Path}");

        // Save the original response body stream
        var originalBodyStream = context.Response.Body;

        try
        {
            // Replace the response body with a new memory stream
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            // Continue processing the request
            await _next(context);

            // Log the HTTP response
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            var responseText = await new StreamReader(context.Response.Body).ReadToEndAsync();
            Console.WriteLine($"Response: {context.Response.StatusCode} - {responseText}");

            // Copy the response back to the original stream
            context.Response.Body.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
        finally
        {
            // Restore the original response body stream
            context.Response.Body = originalBodyStream;
        }
    }
}

// Extension method to use the middleware
public static class LoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseLoggingMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<LoggingMiddleware>();
    }
}
/* Error handeling */
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionHandlingMiddleware(RequestDelegate next)
    {
        _next = next;
    }



    public async Task InvokeAsync(HttpContext context)
{
    try
    {
        await _next(context);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unhandled Exception: {ex.Message}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;

        var errorMessage = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development"
            ? $"An unexpected error occurred: {ex.Message}\n{ex.StackTrace}"
            : "An unexpected error occurred.";

        await context.Response.WriteAsync(errorMessage);
    }
}
}

// Extension method to use the middleware
public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionHandlingMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}

/* Authenticaton */
public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _apiKey;

    public AuthenticationMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _apiKey = configuration["ApiSettings:ApiKey"] ?? throw new InvalidOperationException("API Key is not configured.");
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check for the presence of an "X-API-KEY" header
        if (!context.Request.Headers.TryGetValue("X-API-KEY", out var extractedApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("API Key is missing.");
            return;
        }

        // Validate the API key
        if (!_apiKey.Equals(extractedApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Unauthorized access. Invalid API Key.");
            return;
        }

        // Proceed to the next middleware if the API key is valid
        await _next(context);
    }
}

// Extension method to use the middleware
public static class AuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthenticationMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthenticationMiddleware>();
    }
}