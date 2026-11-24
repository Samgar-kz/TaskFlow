using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaskFlow.Application.Abstractions;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Users;

namespace TaskFlow.Infrastructure.Persistence;

public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Creates a demo team with two populated boards. Never enable in production.</summary>
    public bool Enabled { get; set; }

    /// <summary>Password for every demo user (demo@, priya@, daniel@, emma@taskflow.dev).</summary>
    public string DemoPassword { get; set; } = string.Empty;
}

/// <summary>Seeds a small product team with realistic boards, built through the domain model.</summary>
public sealed class DataSeeder(
    AppDbContext db,
    IPasswordHasher passwordHasher,
    TimeProvider clock,
    IOptions<SeedOptions> options,
    ILogger<DataSeeder> logger)
{
    public const string DemoEmail = "demo@taskflow.dev";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || await db.Users.AnyAsync(u => u.Email == DemoEmail, ct))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.DemoPassword))
        {
            throw new InvalidOperationException("Seed:DemoPassword must be set when seeding is enabled.");
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        User AddUser(string email, string name, string title)
        {
            var user = new User(email, name, title);
            user.SetPasswordHash(passwordHasher.Hash(user, settings.DemoPassword));
            user.CreatedAt = now.AddDays(-60);
            db.Users.Add(user);
            return user;
        }

        var alex = AddUser(DemoEmail, "Alex Morgan", "Full-Stack Developer");
        var priya = AddUser("priya@taskflow.dev", "Priya Shah", "Product Manager");
        var daniel = AddUser("daniel@taskflow.dev", "Daniel Kim", "Backend Developer");
        var emma = AddUser("emma@taskflow.dev", "Emma Wilson", "UX Designer");

        var web = Project.Create(priya.Id, "Website Redesign", "WEB", "New marketing site and customer portal on the design system.", now.AddDays(-30));
        foreach (var member in new[] { alex, daniel, emma })
        {
            web.AddMember(priya.Id, member.Id, now.AddDays(-29));
        }

        var mobile = Project.Create(alex.Id, "Mobile Banking App", "MOB", "Native-feel PWA for account overview and transfers.", now.AddDays(-20));
        mobile.AddMember(alex.Id, daniel.Id, now.AddDays(-19));
        mobile.AddMember(alex.Id, emma.Id, now.AddDays(-19));

        db.Projects.AddRange(web, mobile);

        var columnsByProject = new Dictionary<Guid, Dictionary<string, List<TaskItem>>>();

        void Card(Project project, string column, string title, TaskPriority priority, User? assignee, int? dueInDays, User creator, string? description = null)
        {
            var target = project.Columns.Single(c => c.Name == column);
            var columns = columnsByProject.TryGetValue(project.Id, out var existing) ? existing : columnsByProject[project.Id] = [];
            var list = columns.TryGetValue(column, out var tasks) ? tasks : columns[column] = [];

            var task = new TaskItem(project, project.AllocateTaskNumber(), title, description, priority, assignee?.Id, dueInDays is { } d ? today.AddDays(d) : null, creator.Id);
            BoardOrdering.Add(project, task, target, list, now.AddDays(Math.Min(dueInDays ?? 0, 0)));
            task.CreatedAt = now.AddDays(-14 + list.Count);
            list.Add(task);
            db.Tasks.Add(task);
        }

        Card(web, "Backlog", "Accessibility audit of checkout flow", TaskPriority.Medium, null, null, priya);
        Card(web, "Backlog", "Add French translations", TaskPriority.Low, null, null, priya);
        Card(web, "Backlog", "Evaluate headless CMS options", TaskPriority.Low, daniel, null, priya);
        Card(web, "To Do", "Implement pricing page", TaskPriority.High, alex, 6, priya, "Three plans, monthly/annual toggle, FAQ accordion.");
        Card(web, "To Do", "Newsletter sign-up with double opt-in", TaskPriority.Medium, daniel, 9, priya);
        Card(web, "To Do", "Design 404 and error pages", TaskPriority.Low, emma, 12, priya);
        Card(web, "In Progress", "Customer portal login with SSO", TaskPriority.Urgent, alex, 2, priya, "OIDC with the corporate identity provider; keep a password fallback.");
        Card(web, "In Progress", "Responsive navigation component", TaskPriority.High, emma, 4, priya);
        Card(web, "In Progress", "Contact form API with rate limiting", TaskPriority.Medium, daniel, 5, priya);
        Card(web, "Review", "Homepage hero animation", TaskPriority.Medium, emma, 1, priya);
        Card(web, "Review", "Lighthouse performance fixes", TaskPriority.High, alex, 3, priya);
        Card(web, "Done", "Set up CI/CD pipeline", TaskPriority.High, daniel, -10, priya);
        Card(web, "Done", "Design system tokens", TaskPriority.Medium, emma, -7, priya);
        Card(web, "Done", "Analytics and consent banner", TaskPriority.Medium, alex, -3, priya);

        Card(mobile, "Backlog", "Biometric login", TaskPriority.High, null, null, alex);
        Card(mobile, "To Do", "Transfer between own accounts", TaskPriority.Urgent, daniel, 5, alex);
        Card(mobile, "To Do", "Spending insights screen", TaskPriority.Medium, emma, 10, alex);
        Card(mobile, "In Progress", "Account overview with balances", TaskPriority.High, alex, 3, alex);
        Card(mobile, "In Progress", "Offline mode and sync queue", TaskPriority.Medium, daniel, 8, alex);
        Card(mobile, "Review", "Onboarding screens", TaskPriority.Medium, emma, 2, alex);
        Card(mobile, "Done", "Project skeleton and PWA manifest", TaskPriority.Low, alex, -6, alex);

        await db.SaveChangesAsync(ct);

        var pricing = await db.Tasks.SingleAsync(t => t.ProjectId == web.Id && t.Title == "Implement pricing page", ct);
        var sso = await db.Tasks.SingleAsync(t => t.ProjectId == web.Id && t.Title == "Customer portal login with SSO", ct);
        db.TaskComments.AddRange(
            new TaskComment(pricing.Id, priya.Id, "Copy is final in the brief; annual plan gets a 15% discount badge.", now.AddDays(-3)),
            new TaskComment(pricing.Id, emma.Id, "Mockups are in the design file, mobile layout included.", now.AddDays(-2)),
            new TaskComment(sso.Id, daniel.Id, "Token endpoint is ready on staging.", now.AddHours(-20)));
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Seeded demo team with {Projects} projects", 2);
    }
}
