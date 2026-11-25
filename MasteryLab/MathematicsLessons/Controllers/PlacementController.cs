using MathematicsLessons.Infrastructure.Data;
using MathematicsLessons.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MathematicsLessons.Controllers;

/// Short quiz → recommended level (Brilliant-style "start at the right level").
public class PlacementController : Controller
{
    private readonly ApplicationDbContext _ctx;
    private readonly UserManager<User> _users;

    public PlacementController(ApplicationDbContext ctx, UserManager<User> users)
    {
        _ctx = ctx;
        _users = users;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Placement";
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(int q1, int q2, int q3, int q4, int q5)
    {
        // Each answer 0=easy miss/hard, 1=correct-ish for that band
        // Score maps to recommended SortOrder of Level
        int score = 0;
        // q1: 2+3=?  → 5
        if (q1 == 5) score += 1;
        // q2: 12/3=? → 4
        if (q2 == 4) score += 1;
        // q3: 20% of 50 → 10
        if (q3 == 10) score += 2;
        // q4: x+7=15 → 8
        if (q4 == 8) score += 2;
        // q5: derivative of x^2 is 2x → accept 2 as coded choice
        if (q5 == 2) score += 3;

        // 0-2 Beginner, 3-5 Intermediate, 6-7 Advanced, 8+ College
        int sort = score switch
        {
            <= 2 => 1,
            <= 5 => 2,
            <= 7 => 3,
            _ => 4
        };

        var level = await _ctx.Levels.AsNoTracking()
            .OrderBy(l => l.SortOrder)
            .Skip(sort - 1)
            .FirstOrDefaultAsync()
            ?? await _ctx.Levels.OrderBy(l => l.SortOrder).FirstAsync();

        ViewBag.Score = score;
        ViewBag.LevelName = level.Name;
        ViewBag.LevelId = level.Id;
        ViewBag.GradeRange = level.GradeRange;

        if (User.Identity?.IsAuthenticated == true)
        {
            var me = await _users.GetUserAsync(User);
            if (me != null)
            {
                await _ctx.Notifications.AddAsync(new Notification
                {
                    UserId = me.Id,
                    Title = "Placement result",
                    Body = $"Recommended path: {level.Name} ({level.GradeRange}). Score {score}/9.",
                    Link = $"/Classroom/Level/{level.Id}"
                });
                await _ctx.SaveChangesAsync();
            }
        }

        return View("Result");
    }
}
