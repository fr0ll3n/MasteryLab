using MathematicsLessons.Infrastructure.Data;
using MathematicsLessons.Infrastructure.Data.Entities;
using MathematicsLessons.Services.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MathematicsLessons.Services;

public class BookingService : IBookingService
{
    private readonly ApplicationDbContext _ctx;
    private readonly INotificationService _notify;
    private readonly UserManager<User> _users;

    public BookingService(ApplicationDbContext ctx, INotificationService notify, UserManager<User> users)
    {
        _ctx = ctx;
        _notify = notify;
        _users = users;
    }

    public Task<List<AppointmentPlan>> GetDurationPlansAsync() =>
        _ctx.AppointmentPlans.OrderBy(p => p.DurationMinutes).ToListAsync();

    public async Task<int> SessionsUsedThisMonthAsync(string userId)
    {
        var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        return await _ctx.Appointments.CountAsync(a =>
            a.UserId == userId && a.Date >= monthStart && a.Status != "Cancelled");
    }

    public async Task<List<(string Id, string Name)>> GetTeachersAsync()
    {
        var teachers = await _users.GetUsersInRoleAsync("Teacher");
        var admins = await _users.GetUsersInRoleAsync("Admin");
        var map = new Dictionary<string, string>();
        foreach (var u in teachers.Concat(admins))
        {
            var name = $"{u.FirstName} {u.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(name)) name = u.Email ?? u.UserName ?? u.Id;
            map[u.Id] = name;
        }
        return map.OrderBy(kv => kv.Value).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// Busy window = [booking.Date, booking.Date + 2 hours).
    private static (DateTime BlockStart, DateTime BlockEnd) BusyWindow(Appointment a) =>
        (a.Date, a.Date.AddHours(IBookingService.BusyBlockHours));

    private async Task<bool> TeacherFreeAsync(string teacherId, DateTime start, int durationMinutes)
    {
        // Request occupies [start, start + duration). Also enforce site rule: teacher blocked 2h from each booking start.
        var requestEnd = start.AddMinutes(durationMinutes);
        var active = await _ctx.Appointments.AsNoTracking()
            .Where(a => a.TeacherId == teacherId && a.Status != "Cancelled" && a.Status != "Completed")
            .Where(a => a.Date.Date == start.Date || a.Date >= start.AddHours(-IBookingService.BusyBlockHours))
            .ToListAsync();

        foreach (var a in active)
        {
            var (b0, b1) = BusyWindow(a);
            // overlap between [start, requestEnd) and busy [b0, b1)
            if (start < b1 && requestEnd > b0)
                return false;
        }
        return true;
    }

    public async Task<List<int>> GetAvailableHoursAsync(string teacherId, DateTime day, int durationMinutes)
    {
        var hours = new List<int>();
        if (day.Date < DateTime.Today) return hours;

        // Last start so session ends by DayEndHour (23:00)
        var lastStart = IBookingService.DayEndHour - (int)Math.Ceiling(durationMinutes / 60.0);
        if (lastStart < IBookingService.DayStartHour) return hours;

        for (var h = IBookingService.DayStartHour; h <= lastStart; h++)
        {
            var start = day.Date.AddHours(h);
            if (start < DateTime.Now.AddHours(2)) continue; // min 2h lead time
            if (start.Hour < IBookingService.DayStartHour || start.Hour >= IBookingService.DayEndHour) continue;
            if (await TeacherFreeAsync(teacherId, start, durationMinutes))
                hours.Add(h);
        }
        return hours;
    }

    public async Task<(bool Ok, string Message, Appointment? Appointment)> CompletePaidBookingAsync(
        User user, DateTime start, int planId, string teacherId, string? notes)
    {
        if (user.SubId == null || user.Sub == null || !user.Sub.IncludesPrivateSessions)
            return (false, "Private sessions are only for Monthly / Annual subscribers.", null);

        var plan = await _ctx.AppointmentPlans.FindAsync(planId);
        if (plan == null) return (false, "Invalid duration plan.", null);

        if (string.IsNullOrWhiteSpace(teacherId))
            return (false, "Please select a teacher.", null);

        var teacher = await _users.FindByIdAsync(teacherId);
        if (teacher == null) return (false, "Teacher not found.", null);

        if (start < DateTime.Now.AddHours(2))
            return (false, "Pick a time at least 2 hours from now.", null);

        if (start.Hour < IBookingService.DayStartHour || start.Hour >= IBookingService.DayEndHour)
            return (false, "Sessions can only start between 08:00 and 23:00.", null);

        var end = start.AddMinutes(plan.DurationMinutes);
        if (end.Hour > IBookingService.DayEndHour || (end.Hour == IBookingService.DayEndHour && end.Minute > 0) || end.Date > start.Date)
            return (false, "Session must finish by 23:00 the same day.", null);

        var used = await SessionsUsedThisMonthAsync(user.Id);
        if (used >= user.Sub.MaxSessionsPerMonth)
            return (false, $"Monthly private-session limit reached ({user.Sub.MaxSessionsPerMonth}).", null);

        if (!await TeacherFreeAsync(teacherId, start, plan.DurationMinutes))
            return (false, "That teacher is not available in this 2-hour window. Pick another time or teacher.", null);

        await using var tx = await _ctx.Database.BeginTransactionAsync();

        var receipt = new Receipt
        {
            SubTotal = plan.Price,
            Kind = "Session",
            CreatedAt = DateTime.UtcNow
        };
        _ctx.Receipts.Add(receipt);
        await _ctx.SaveChangesAsync();

        var meetCode = Guid.NewGuid().ToString("N")[..10];
        var appt = new Appointment
        {
            UserId = user.Id,
            TeacherId = teacherId,
            Date = start,
            DurationMinutes = plan.DurationMinutes,
            AppointmentPlanId = plan.Id,
            ReceiptId = receipt.Id,
            Status = "Booked",
            Notes = notes,
            MeetingLink = $"https://meet.google.com/{meetCode}"
        };
        _ctx.Appointments.Add(appt);
        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        await _notify.NotifyAsync(user.Id, "Private session booked",
            $"{start:dd MMM HH:mm} · {plan.Name} with teacher. Meet link on session page.",
            $"/Booking/Session/{appt.Id}");

        return (true, "Booked successfully.", appt);
    }
}
