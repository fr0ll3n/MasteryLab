using MathematicsLessons.Infrastructure.Data.Entities;

namespace MathematicsLessons.Services.Interfaces;

public interface IBookingService
{
    /// Fixed 2-hour busy block after a booking starts (regardless of session length).
    const int BusyBlockHours = 2;
    const int DayStartHour = 8;
    const int DayEndHour = 23; // exclusive — last start depends on duration

    Task<List<AppointmentPlan>> GetDurationPlansAsync();
    Task<int> SessionsUsedThisMonthAsync(string userId);
    Task<List<(string Id, string Name)>> GetTeachersAsync();
    Task<List<int>> GetAvailableHoursAsync(string teacherId, DateTime day, int durationMinutes);
    Task<(bool Ok, string Message, Appointment? Appointment)> CompletePaidBookingAsync(
        User user, DateTime start, int planId, string teacherId, string? notes);
}
