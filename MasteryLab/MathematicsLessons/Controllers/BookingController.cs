using MathematicsLessons.Infrastructure.Data;
using MathematicsLessons.Infrastructure.Data.Entities;
using MathematicsLessons.Models;
using MathematicsLessons.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MathematicsLessons.Controllers;

/// Private 1:1 sessions — Monthly/Annual subscribers only (hidden from Free).
[Authorize]
public class BookingController : Controller
{
    private readonly ApplicationDbContext _ctx;
    private readonly UserManager<User> _users;
    private readonly IBookingService _booking;
    private readonly ISubscriptionService _subs;

    public BookingController(ApplicationDbContext ctx, UserManager<User> users, IBookingService booking, ISubscriptionService subs)
    {
        _ctx = ctx; _users = users; _booking = booking; _subs = subs;
    }

    private async Task<User?> MeAsync()
    {
        var me = await _users.GetUserAsync(User);
        if (me == null) return null;
        return await _ctx.Users.Include(u => u.Sub).FirstAsync(u => u.Id == me.Id);
    }

    private async Task<IActionResult?> EnsureSubscriberAsync(User me)
    {
        if (_subs.CanBookPrivateSessions(me)) return null;
        TempData["Error"] = "Private lessons are only for Monthly / Annual subscribers. Free accounts cannot book.";
        return RedirectToAction("Plans", "Subscription");
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var me = await MeAsync();
        if (me == null) return Challenge();
        var gate = await EnsureSubscriberAsync(me);
        if (gate != null) return gate;

        var used = await _booking.SessionsUsedThisMonthAsync(me.Id);
        var mine = await _ctx.Appointments.Include(a => a.AppointmentPlan).Include(a => a.Receipt)
            .Where(a => a.UserId == me.Id).OrderByDescending(a => a.Date).ToListAsync();
        var upcoming = mine.Where(a => a.Status is "Booked" or "InProgress" && a.Date.AddMinutes(a.DurationMinutes) >= DateTime.Now)
            .OrderBy(a => a.Date).FirstOrDefault();

        return View(new BookingPage
        {
            IsSubscribed = true,
            PlanName = me.Sub!.Type,
            Plans = await _booking.GetDurationPlansAsync(),
            Mine = mine,
            PublicSessions = await _ctx.Schedules.Where(s => s.IsPublic && s.SelectedDateTime > DateTime.Now)
                .OrderBy(s => s.SelectedDateTime).Take(5).ToListAsync(),
            SessionsLeftThisMonth = Math.Max(0, me.Sub.MaxSessionsPerMonth - used),
            Upcoming = upcoming
        });
    }

    [HttpGet]
    public async Task<IActionResult> Book(int? planId, string? date, string? teacherId)
    {
        var me = await MeAsync();
        if (me == null) return Challenge();
        var gate = await EnsureSubscriberAsync(me);
        if (gate != null) return gate;

        var plans = await _booking.GetDurationPlansAsync();
        var teachers = (await _booking.GetTeachersAsync())
            .Select(x => new TeacherOption { Id = x.Id, Name = x.Name }).ToList();
        ViewBag.Plans = plans;
        ViewBag.Teachers = teachers;
        ViewBag.SelectedPlanId = planId;
        ViewBag.SelectedTeacherId = teacherId;
        ViewBag.MinDate = DateTime.Today.AddDays(0).ToString("yyyy-MM-dd");
        ViewBag.MaxDate = DateTime.Today.AddDays(60).ToString("yyyy-MM-dd");
        ViewBag.TimeZoneNote = "Hours are local (EET/EEST). Sessions only 08:00–23:00. Each booking blocks the teacher for 2 hours from the start time.";

        AppointmentPlan? selected = planId != null ? plans.FirstOrDefault(p => p.Id == planId) : null;
        ViewBag.SelectedPlan = selected;

        DateTime? selectedDate = null;
        if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var d))
            selectedDate = d.Date;
        ViewBag.SelectedDate = selectedDate?.ToString("yyyy-MM-dd");

        var hours = new List<int>();
        if (selected != null && selectedDate != null && !string.IsNullOrEmpty(teacherId))
            hours = await _booking.GetAvailableHoursAsync(teacherId, selectedDate.Value, selected.DurationMinutes);
        ViewBag.Hours = hours.Select(h => h.ToString("00") + ":00").ToList();
        ViewBag.HourValues = hours;

        return View();
    }

    /// JSON hours for AJAX calendar.
    [HttpGet]
    public async Task<IActionResult> AvailableHours(string teacherId, string date, int planId)
    {
        var me = await MeAsync();
        if (me == null || !_subs.CanBookPrivateSessions(me))
            return Json(new { ok = false, hours = Array.Empty<int>() });
        if (!DateTime.TryParse(date, out var d))
            return Json(new { ok = false, hours = Array.Empty<int>() });
        var plan = (await _booking.GetDurationPlansAsync()).FirstOrDefault(p => p.Id == planId);
        if (plan == null || string.IsNullOrEmpty(teacherId))
            return Json(new { ok = false, hours = Array.Empty<int>() });
        var hours = await _booking.GetAvailableHoursAsync(teacherId, d.Date, plan.DurationMinutes);
        return Json(new { ok = true, hours });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(DateTime date, string hour, int planId, string teacherId, string? notes)
    {
        var me = await MeAsync();
        if (me == null) return Challenge();
        var gate = await EnsureSubscriberAsync(me);
        if (gate != null) return gate;

        if (!TimeSpan.TryParse(hour.Length <= 2 ? hour + ":00" : hour, out var time))
        {
            TempData["Error"] = "Invalid hour.";
            return RedirectToAction(nameof(Book), new { planId, date = date.ToString("yyyy-MM-dd"), teacherId });
        }

        var start = date.Date.Add(time);
        var plan = (await _booking.GetDurationPlansAsync()).FirstOrDefault(p => p.Id == planId);
        if (plan == null)
        {
            TempData["Error"] = "Invalid duration.";
            return RedirectToAction(nameof(Book));
        }

        HttpContext.Session.SetString("PendingBook", System.Text.Json.JsonSerializer.Serialize(new PendingBookDto
        {
            Date = start,
            PlanId = plan.Id,
            TeacherId = teacherId,
            Notes = notes,
            Price = plan.Price,
            PlanName = plan.Name,
            DurationMinutes = plan.DurationMinutes
        }));
        return RedirectToAction(nameof(Pay));
    }

    [HttpGet]
    public IActionResult Pay()
    {
        var json = HttpContext.Session.GetString("PendingBook");
        if (string.IsNullOrEmpty(json))
        {
            TempData["Error"] = "No pending booking.";
            return RedirectToAction(nameof(Book));
        }
        ViewBag.Pending = System.Text.Json.JsonSerializer.Deserialize<PendingBookDto>(json);
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmPayment()
    {
        var me = await MeAsync();
        if (me == null) return Challenge();
        var json = HttpContext.Session.GetString("PendingBook");
        if (string.IsNullOrEmpty(json)) return RedirectToAction(nameof(Book));
        var pending = System.Text.Json.JsonSerializer.Deserialize<PendingBookDto>(json)!;

        var (ok, msg, appt) = await _booking.CompletePaidBookingAsync(
            me, pending.Date, pending.PlanId, pending.TeacherId, pending.Notes);
        HttpContext.Session.Remove("PendingBook");
        TempData[ok ? "Success" : "Error"] = msg;
        if (ok && appt != null)
        {
            TempData["PlayNotify"] = "1";
            return RedirectToAction(nameof(Session), new { id = appt.Id });
        }
        return RedirectToAction(nameof(Book));
    }

    [HttpGet]
    public async Task<IActionResult> Session(int id)
    {
        var me = await MeAsync();
        if (me == null) return Challenge();
        var appt = await _ctx.Appointments.Include(a => a.AppointmentPlan).Include(a => a.Receipt)
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == me.Id);
        if (appt == null) return NotFound();
        return View(appt);
    }

    public class PendingBookDto
    {
        public DateTime Date { get; set; }
        public int PlanId { get; set; }
        public string TeacherId { get; set; } = "";
        public string? Notes { get; set; }
        public decimal Price { get; set; }
        public string PlanName { get; set; } = "";
        public int DurationMinutes { get; set; }
    }
}
