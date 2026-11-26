using MathematicsLessons.Infrastructure.Data;
using MathematicsLessons.Infrastructure.Data.Entities;
using MathematicsLessons.Models;
using MathematicsLessons.Services;
using MathematicsLessons.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace MathematicsLessons.Controllers;

/// Course path: levels → topics → lessons → progress (Khan-style unlock).
[Authorize]
public class ClassroomController : Controller
{
    private readonly ApplicationDbContext _ctx;
    private readonly UserManager<User> _users;
    private readonly IWebHostEnvironment _env;
    private readonly ILessonAccessService _access;
    private readonly IProgressService _progress;
    private readonly ICertificateService _certs;
    private readonly INotificationService _notify;
    private readonly ISubscriptionService _subs;
    private readonly IPointsService _points;

    public ClassroomController(
        ApplicationDbContext ctx, UserManager<User> users, IWebHostEnvironment env,
        ILessonAccessService access, IProgressService progress, ICertificateService certs,
        INotificationService notify, ISubscriptionService subs, IPointsService points)
    {
        _ctx = ctx; _users = users; _env = env;
        _access = access; _progress = progress; _certs = certs;
        _notify = notify; _subs = subs; _points = points;
    }

    
    /// Lessons the free user has already started (counts toward the 3-video free quota).
    private async Task<HashSet<int>> GetStartedLessonIdsAsync(string userId)
    {
        return (await _ctx.LessonProgress.AsNoTracking()
            .Where(p => p.UserId == userId && (p.VideoWatched || p.WatchedPercent >= 5 || p.LastPositionSeconds > 3))
            .Select(p => p.LessonId)
            .ToListAsync()).ToHashSet();
    }

    private bool IsStaffUser() => User.IsInRole("Admin") || User.IsInRole("Teacher");

    private async Task<User?> MeAsync()
    {
        var me = await _users.GetUserAsync(User);
        if (me == null) return null;
        return await _ctx.Users.Include(u => u.Sub).FirstAsync(u => u.Id == me.Id);
    }

    private bool IsStaff() => User.IsInRole("Teacher") || User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> Index(string? q)
    {
        var me = await MeAsync();
        /* Free: 3 videos max. Paid: full open catalog */

        var levels = await _ctx.Levels.OrderBy(l => l.SortOrder).ToListAsync();
        var topics = await _ctx.Topics.OrderBy(t => t.SortOrder).ToListAsync();
        var lessons = await _ctx.Lessons.OrderBy(l => l.SortOrder).ToListAsync();
        var completed = me == null ? new HashSet<int>() : await _progress.GetCompletedLessonIdsAsync(me.Id);
        var certs = me == null
            ? new HashSet<int>()
            : (await _ctx.Certificates.Where(c => c.UserId == me.Id).Select(c => c.LevelId).ToListAsync()).ToHashSet();

        var isSub = me != null && _subs.IsSubscribed(me);
        var isStaff = IsStaff();
        // Free: up to 3 started videos. Subscriber/Staff: all open (no sequential gates).
        var started = me == null ? new HashSet<int>() : await GetStartedLessonIdsAsync(me.Id);

        var page = new ClassroomPage
        {
            IsSubscribed = isSub || isStaff,
            TotalCompleted = completed.Count,
            TotalLessons = lessons.Count
        };

        foreach (var level in levels)
        {
            var levelLessons = lessons.Where(l => l.LevelId == level.Id).ToList();
            var block = new LevelBlock
            {
                Level = level,
                IsUnlocked = true, // levels always browsable; free quota enforced per lesson
                Completed = levelLessons.Count(l => completed.Contains(l.Id)),
                Total = levelLessons.Count,
                HasCertificate = certs.Contains(level.Id)
            };
            foreach (var topic in topics.Where(t => t.LevelId == level.Id))
            {
                var tb = new TopicBlock { Topic = topic };
                foreach (var lesson in levelLessons.Where(l => l.TopicId == topic.Id).OrderBy(l => l.SortOrder))
                {
                    bool lessonUnlocked = isStaff || _access.CanOpenLesson(isSub, lesson.Id, started);
                    tb.Lessons.Add(new LessonRow
                    {
                        Lesson = lesson,
                        IsUnlocked = lessonUnlocked,
                        IsCompleted = completed.Contains(lesson.Id)
                    });
                }
                block.Topics.Add(tb);
            }
            page.Levels.Add(block);
        }
        
        page.SearchQuery = string.IsNullOrWhiteSpace(q) ? null : q.Trim();
        if (!string.IsNullOrEmpty(page.SearchQuery))
        {
            var term = page.SearchQuery.ToLowerInvariant();
            foreach (var block in page.Levels)
            {
                foreach (var tb in block.Topics)
                {
                    foreach (var row in tb.Lessons)
                    {
                        var hay = $"{row.Lesson.Title} {tb.Topic.Name} {block.Level.Name}".ToLowerInvariant();
                        if (hay.Contains(term))
                        {
                            page.SearchHits.Add(new SearchHit
                            {
                                LessonId = row.Lesson.Id,
                                LessonTitle = row.Lesson.Title,
                                LevelName = block.Level.Name,
                                TopicName = tb.Topic.Name,
                                IsUnlocked = row.IsUnlocked,
                                IsCompleted = row.IsCompleted
                            });
                        }
                    }
                }
            }
        }

        return View(page);

    }


    [HttpGet]
    public async Task<IActionResult> Level(int id)
    {
        var me = await MeAsync();
        var levels = await _ctx.Levels.OrderBy(l => l.SortOrder).ToListAsync();
        var level = levels.FirstOrDefault(l => l.Id == id);
        if (level == null) return NotFound();

        var lessons = await _ctx.Lessons.OrderBy(l => l.SortOrder).ToListAsync();
        var topics = await _ctx.Topics.Where(t => t.LevelId == id).OrderBy(t => t.SortOrder).ToListAsync();
        var completed = me == null ? new HashSet<int>() : await _progress.GetCompletedLessonIdsAsync(me.Id);
        var isSub = me != null && _subs.IsSubscribed(me);
        var isStaff = IsStaff();

        var started = me == null ? new HashSet<int>() : await GetStartedLessonIdsAsync(me.Id);
        var levelUnlocked = true;

        var levelLessons = lessons.Where(l => l.LevelId == id).ToList();
        var block = new LevelBlock
        {
            Level = level,
            IsUnlocked = levelUnlocked,
            Completed = levelLessons.Count(l => completed.Contains(l.Id)),
            Total = levelLessons.Count,
            HasCertificate = me != null && await _ctx.Certificates.AnyAsync(c => c.UserId == me.Id && c.LevelId == id)
        };
        foreach (var topic in topics)
        {
            var tb = new TopicBlock { Topic = topic };
            foreach (var lesson in levelLessons.Where(l => l.TopicId == topic.Id).OrderBy(l => l.SortOrder))
            {
                var unlocked = isStaff || _access.CanOpenLesson(isSub, lesson.Id, started);
                tb.Lessons.Add(new LessonRow
                {
                    Lesson = lesson,
                    IsUnlocked = unlocked,
                    IsCompleted = completed.Contains(lesson.Id)
                });
            }
            block.Topics.Add(tb);
        }
        ViewBag.IsSubscribed = isSub || isStaff;
        return View(block);
    }

    [HttpGet]
    public async Task<IActionResult> Lesson(int id)
    {
        var me = await MeAsync();
        var lesson = await _ctx.Lessons
            .Include(l => l.Topic).Include(l => l.Level).Include(l => l.Exercises)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (lesson == null) return NotFound();

        var levels = await _ctx.Levels.OrderBy(l => l.SortOrder).ToListAsync();
        var lessons = await _ctx.Lessons.OrderBy(l => l.SortOrder).ToListAsync();
        var completed = me == null ? new HashSet<int>() : await _progress.GetCompletedLessonIdsAsync(me.Id);

        // Subscriber/Staff: full access. Free: only 3 videos (any courses), then lock.
        var isSubUser = me != null && _subs.IsSubscribed(me);
        var startedLessons = me == null ? new HashSet<int>() : await GetStartedLessonIdsAsync(me.Id);
        bool unlocked = IsStaff() || _access.CanOpenLesson(isSubUser, lesson.Id, startedLessons);
        if (!unlocked)
        {
            if (me == null) return Challenge();
            TempData["Error"] = "Free plan includes 3 video lessons. Subscribe Monthly or Annual (PayPal checkout) for full access.";
            return RedirectToAction("Plans", "Subscription");
        }

        LessonProgress? prog = null;
        if (me != null)
            prog = await _ctx.LessonProgress.FirstOrDefaultAsync(p => p.UserId == me.Id && p.LessonId == id);

        var next = lessons
            .Where(l => l.LevelId == lesson.LevelId && l.SortOrder > lesson.SortOrder)
            .OrderBy(l => l.SortOrder).FirstOrDefault();

        var watched = prog?.VideoWatched ?? false;
        var passed = prog?.ExercisesPassed ?? false;
        return View(new LessonPage
        {
            Lesson = lesson,
            IsUnlocked = unlocked,
            IsCompleted = prog?.IsCompleted ?? false,
            WatchedPercent = prog?.WatchedPercent ?? 0,
            ResumeSeconds = (prog != null && !prog.VideoWatched) ? prog.LastPositionSeconds : 0,
            NextLesson = next,
            CanUpload = IsStaff(),
            VideoWatched = watched,
            ExercisesPassed = passed,
            CanPractice = watched && !passed,
            Exercises = lesson.Exercises.OrderBy(e => e.Id).ToList()
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Progress(int id, int percent)
    {
        var me = await MeAsync();
        if (me == null) return Challenge();
        var lesson = await _ctx.Lessons.FindAsync(id);
        if (lesson == null) return NotFound();

        var levels = await _ctx.Levels.OrderBy(l => l.SortOrder).ToListAsync();
        var lessons = await _ctx.Lessons.OrderBy(l => l.SortOrder).ToListAsync();
        var completed = await _progress.GetCompletedLessonIdsAsync(me.Id);

        var startedP = await GetStartedLessonIdsAsync(me.Id);
        var canProgress = IsStaff() || _access.CanOpenLesson(_subs.IsSubscribed(me), id, startedP);
        if (!canProgress) return Forbid();

        var wasDone = completed.Contains(id);
        var row = await _progress.RecordWatchAsync(me.Id, id, percent);
        if (row.IsCompleted && !wasDone)
        {
            await _certs.TryIssueForLevelAsync(me.Id, lesson.LevelId);
        }
        return RedirectToAction(nameof(Lesson), new { id });
    }

    [Authorize(Roles = "Teacher,Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(107374182400)]
    [RequestFormLimits(MultipartBodyLengthLimit = 107374182400)]
    public async Task<IActionResult> Upload(int id, IFormFile video)
    {
        var lesson = await _ctx.Lessons.FindAsync(id);
        if (lesson == null || video == null || video.Length == 0) return NotFound();

        var dir = Path.Combine(_env.WebRootPath, "uploads", "lessons");
        Directory.CreateDirectory(dir);
        var name = $"{id}_{Guid.NewGuid():N}{Path.GetExtension(video.FileName)}";
        var path = Path.Combine(dir, name);
        await using (var fs = System.IO.File.Create(path))
            await video.CopyToAsync(fs);
        lesson.Video = $"/uploads/lessons/{name}";
        await _ctx.SaveChangesAsync();
        TempData["Success"] = "Video uploaded.";
        return RedirectToAction(nameof(Lesson), new { id });
    }

    [Authorize(Roles = "Teacher,Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveVideo(int id)
    {
        var lesson = await _ctx.Lessons.FindAsync(id);
        if (lesson == null) return NotFound();
        if (!string.IsNullOrEmpty(lesson.Video) && lesson.Video.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            var physical = Path.Combine(_env.WebRootPath, lesson.Video.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(physical))
                System.IO.File.Delete(physical);
        }
        lesson.Video = null;
        await _ctx.SaveChangesAsync();
        TempData["Success"] = "Video removed. You can upload a new one.";
        return RedirectToAction(nameof(Lesson), new { id });
    }


    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkVideoWatched(int id)
    {
        var me = await MeAsync();
        if (me == null) return Challenge();
        var lesson = await _ctx.Lessons.FindAsync(id);
        if (lesson == null) return NotFound();
        var progress = await _ctx.LessonProgress.FirstOrDefaultAsync(p => p.UserId == me.Id && p.LessonId == id);
        if (progress == null)
        {
            progress = new LessonProgress { UserId = me.Id, LessonId = id, VideoWatched = true, WatchedPercent = 100 };
            _ctx.LessonProgress.Add(progress);
        }
        else
        {
            progress.VideoWatched = true;
            progress.WatchedPercent = 100;
            progress.UpdatedAt = DateTime.UtcNow;
        }
        await _ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Lesson), new { id });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> SubmitExercises(int id, List<string> answers)
    {
        var me = await MeAsync();
        if (me == null) return Challenge();
        var lesson = await _ctx.Lessons.Include(l => l.Exercises).FirstOrDefaultAsync(l => l.Id == id);
        if (lesson == null) return NotFound();
        var progress = await _ctx.LessonProgress.FirstOrDefaultAsync(p => p.UserId == me.Id && p.LessonId == id);
        if (progress == null || !progress.VideoWatched)
        {
            TempData["Error"] = "Watch the full video first.";
            return RedirectToAction(nameof(Lesson), new { id });
        }

        var alreadyComplete = progress.IsCompleted;
        var exercises = lesson.Exercises.OrderBy(e => e.Id).ToList();
        bool allOk = true;
        if (exercises.Count > 0)
        {
            for (int i = 0; i < exercises.Count; i++)
            {
                var given = answers != null && i < answers.Count ? answers[i] : "";
                var ok = AnswerChecker.IsCorrect(exercises[i].Solution, given);
                if (!ok) allOk = false;
                _ctx.ExerciseAttempts.Add(new ExerciseAttempt
                {
                    UserId = me.Id,
                    ExerciseId = exercises[i].Id,
                    LessonId = lesson.Id,
                    GivenAnswer = given ?? "",
                    IsCorrect = ok
                });
            }
        }

        if (!allOk)
        {
            progress.VideoWatched = false;
            progress.ExercisesPassed = false;
            progress.IsCompleted = false;
            progress.WatchedPercent = 0;
            progress.UpdatedAt = DateTime.UtcNow;
            await _ctx.SaveChangesAsync();
            TempData["Error"] = "Wrong answers. Watch the video again, then retry.";
            return RedirectToAction(nameof(Lesson), new { id });
        }

        progress.ExercisesPassed = true;
        progress.IsCompleted = true;
        progress.WatchedPercent = 100;
        progress.CompletedAt = DateTime.UtcNow;
        progress.UpdatedAt = DateTime.UtcNow;
        await _ctx.SaveChangesAsync();

        // Points + streak only the first time the lesson is completed
        if (!alreadyComplete)
        {
            var pts = lesson.PointsReward > 0 ? lesson.PointsReward : 25;
            if (exercises.Count > 0)
                pts += exercises.Sum(e => e.PointsReward > 0 ? e.PointsReward : 5);
            await _points.AwardAsync(me, pts, $"Completed lesson: {lesson.Title}");

            var cert = await _certs.TryIssueForLevelAsync(me.Id, lesson.LevelId);
            if (cert != null)
                await _notify.NotifyAsync(me.Id, "Certificate earned",
                    $"You completed a full level and earned a certificate.",
                    "/Account/Profile");
            else
                await _notify.NotifyAsync(me.Id, "Lesson complete",
                    $"{lesson.Title} — next lesson unlocked. +{pts} points.",
                    $"/Classroom/Lesson/{id}");
        }

        TempData["Success"] = alreadyComplete
            ? "Lesson already completed."
            : "Exercises passed. Next lesson unlocked.";
        return RedirectToAction(nameof(Lesson), new { id });
    }


    /// Periodic save of playback position (resume later).
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveVideoProgress(int id, double position, int percent)
    {
        var me = await MeAsync();
        if (me == null) return Unauthorized();
        var lesson = await _ctx.Lessons.FindAsync(id);
        if (lesson == null) return NotFound();

        var progress = await _ctx.LessonProgress.FirstOrDefaultAsync(p => p.UserId == me.Id && p.LessonId == id);
        if (progress == null)
        {
            progress = new LessonProgress { UserId = me.Id, LessonId = id };
            _ctx.LessonProgress.Add(progress);
        }
        if (!progress.VideoWatched)
        {
            progress.LastPositionSeconds = Math.Max(0, position);
            progress.WatchedPercent = Math.Clamp(percent, 0, 99);
            progress.UpdatedAt = DateTime.UtcNow;
            await _ctx.SaveChangesAsync();
        }
        return Ok();
    }

    /// Weak-spot review: latest wrong answers for this user.
    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Review()
    {
        var me = await MeAsync();
        if (me == null) return Challenge();

        var wrong = await _ctx.ExerciseAttempts.AsNoTracking()
            .Include(a => a.Exercise)
            .Include(a => a.Lesson)
            .Where(a => a.UserId == me.Id && !a.IsCorrect)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .ToListAsync();

        // Prefer unique exercises (latest wrong per exercise)
        var latest = wrong
            .GroupBy(a => a.ExerciseId)
            .Select(g => g.First())
            .ToList();

        return View(latest);
    }
}
