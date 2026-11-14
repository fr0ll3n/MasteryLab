using Microsoft.Extensions.Configuration;
using System.ComponentModel.DataAnnotations;
using MathematicsLessons.Infrastructure.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace MathematicsLessons.Areas.Identity.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IConfiguration _config;

    public RegisterModel(UserManager<User> userManager, SignInManager<User> signInManager, RoleManager<IdentityRole> roleManager, IConfiguration config)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _roleManager = roleManager;
        _config = config;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required, MaxLength(50)]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = "";

        [Required, MaxLength(50)]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = "";

        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required, StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password")]
        public string ConfirmPassword { get; set; } = "";
    }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        if (!ModelState.IsValid) return Page();

        var user = new User
        {
            UserName = Input.Email,
            Email = Input.Email,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
            EmailConfirmed = true
        };

        var result = await _userManager.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            if (!await _roleManager.RoleExistsAsync("Student"))
                await _roleManager.CreateAsync(new IdentityRole("Student"));
            if (!await _userManager.IsInRoleAsync(user, "Student"))
                await _userManager.AddToRoleAsync(user, "Student");

            // Owner email from config → Admin + Teacher
            var ownerEmail = _config["OwnerEmail"];
            if (!string.IsNullOrWhiteSpace(ownerEmail)
                && string.Equals(ownerEmail.Trim(), Input.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                foreach (var role in new[] { "Admin", "Teacher" })
                {
                    if (!await _roleManager.RoleExistsAsync(role))
                        await _roleManager.CreateAsync(new IdentityRole(role));
                    if (!await _userManager.IsInRoleAsync(user, role))
                        await _userManager.AddToRoleAsync(user, role);
                }
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect(returnUrl);
        }
        foreach (var e in result.Errors)
            ModelState.AddModelError(string.Empty, e.Description);
        return Page();
    }
}
