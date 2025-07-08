var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseStaticFiles(); // 👈 This enables serving HTML, CSS, JS from wwwroot

app.MapFallbackToFile("index.html"); // Optional: Serve index.html by default

app.Run();