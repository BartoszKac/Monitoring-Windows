using Microsoft.EntityFrameworkCore;
using SerwerWeb.Controllers;
using webserwer.Controllers; // To musi pasować do namespace z kroku wyżej
using webserwer.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=MonitoringStudentow;Trusted_Connection=True;MultipleActiveResultSets=true"));

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR(); // --- DODAJ TO TUTAJ ---

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

//app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Nadzor}/{action=Index}/{id?}");


app.MapHub<NadzorHub>("/nadzorHub");

app.Run();