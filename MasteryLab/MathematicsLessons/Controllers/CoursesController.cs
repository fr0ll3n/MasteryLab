using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MathematicsLessons.Controllers;

/// Legacy routes — everything goes through Classroom (single unlock path).
[Authorize]
public class CoursesController : Controller
{
    [HttpGet]
    public IActionResult Dashboard() => RedirectToAction("Index", "Classroom");

    [HttpGet]
    public IActionResult LessonsByLevel(int id) => RedirectToAction("Level", "Classroom", new { id });

    [HttpGet]
    public IActionResult LessonDetails(int id) => RedirectToAction("Lesson", "Classroom", new { id });
}
