using Microsoft.EntityFrameworkCore;
using webserwer.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Rejestracja bazy danych SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=MonitoringStudentow;Trusted_Connection=True;MultipleActiveResultSets=true"));

// 2. Dodanie obsługi MVC
builder.Services.AddControllersWithViews();

var app = builder.Build();

// 3. Konfiguracja potoku HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

// 4. Mapowanie tras
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();