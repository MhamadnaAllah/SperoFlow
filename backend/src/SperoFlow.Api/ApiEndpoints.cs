using System.Data;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SperoFlow.Application;
using SperoFlow.Contracts;
using SperoFlow.Domain;
using SperoFlow.Infrastructure;

namespace SperoFlow.Api;

public static partial class ApiEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapSperoFlowEndpoints(this IEndpointRouteBuilder app)
    {
        MapAuthentication(app);

        var api = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .RequireRateLimiting("api")
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        MapIdentityAdministration(api);

        MapProjects(api);
        MapLifeRoles(api);
        MapRoleDiscovery(api);
        MapGoals(api);
        MapGoalRoadmapProposals(api);
        MapEisenhowerProposals(api);
        MapTaskSchedulingProposals(api);
        MapTasks(api);
        MapCalendar(api);
        MapHabits(api);
        MapJournal(api);
        MapJournalInsights(api);
        MapDocuments(api);
        MapKnowledgeCatalog(api);
        MapCoach(api);
        MapAi(api);
        MapAiActionProposals(api);
        MapInternalJobs(app);
        return app;
    }

    private static void MapAuthentication(IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");
        auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new CsrfTokenResponse(tokens.RequestToken ?? string.Empty));
        }).AllowAnonymous();

        auth.MapPost("/register", RegisterAsync)
            .AllowAnonymous()
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        auth.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        auth.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager) =>
            {
                await signInManager.SignOutAsync();
                return Results.NoContent();
            })
            .RequireAuthorization()
            .AddEndpointFilter<AntiforgeryValidationFilter>();
        auth.MapPost("/confirm-email", ConfirmEmailAsync).AllowAnonymous();
        auth.MapGet("/me", (ICurrentUser currentUser, UserManager<ApplicationUser> userManager) => GetCurrentUserAsync(currentUser, userManager))
            .RequireAuthorization();
    }

    private static void MapIdentityAdministration(RouteGroupBuilder api)
    {
        var identity = api.MapGroup("/admin/identity").RequireAuthorization("admin");
        identity.MapPut("/users/{id:guid}/knowledge-role", async (
            Guid id,
            UpdateKnowledgePortalRoleRequest request,
            AppDbContext db,
            ICurrentUser currentUser,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            CancellationToken cancellationToken) =>
        {
            var role = request.Role?.Trim() ?? string.Empty;
            if (role is not ("KnowledgeOwner" or "KnowledgeAdmin"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Role must be KnowledgeOwner or KnowledgeAdmin."] });
            }

            var user = await userManager.FindByIdAsync(id.ToString("D", System.Globalization.CultureInfo.InvariantCulture));
            if (user is null)
            {
                return Results.NotFound();
            }

            return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    if (!await roleManager.RoleExistsAsync(role))
                    {
                        var createRole = await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.CreateVersion7() });
                        if (!createRole.Succeeded)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return Results.Problem(title: "Knowledge role administration is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
                        }
                    }

                    var hasRole = await userManager.IsInRoleAsync(user, role);
                    if (request.Enabled && !hasRole)
                    {
                        var addRole = await userManager.AddToRoleAsync(user, role);
                        if (!addRole.Succeeded)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return Results.ValidationProblem(addRole.Errors.ToDictionary(error => error.Code, error => new[] { error.Description }));
                        }
                    }
                    else if (!request.Enabled && hasRole)
                    {
                        var removeRole = await userManager.RemoveFromRoleAsync(user, role);
                        if (!removeRole.Succeeded)
                        {
                            await transaction.RollbackAsync(cancellationToken);
                            return Results.ValidationProblem(removeRole.Errors.ToDictionary(error => error.Code, error => new[] { error.Description }));
                        }
                    }

                    AddAudit(db, currentUser.UserId, "identity", request.Enabled ? "knowledge_role_granted" : "knowledge_role_revoked", "user", user.Id);
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    var roles = await userManager.GetRolesAsync(user);
                    return Results.Ok(new AuthenticatedUserResponse(user.Id, user.Email ?? string.Empty, user.DisplayName, user.EmailConfirmed, roles.ToArray()));
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            });
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return Results.Problem(title: "Knowledge role administration is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
    }
    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IAccountEmailSender emailSender,
        IOptions<AccountOptions> accounts,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var accountOptions = accounts.Value;
        var bootstrapRegistration = !accountOptions.AllowPublicRegistration;
        if (bootstrapRegistration && !BootstrapTokenMatches(request.BootstrapToken, accountOptions.BootstrapRegistrationTokenPath))
        {
            return Results.Problem(title: "Registration is closed.", statusCode: StatusCodes.Status403Forbidden);
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (!IsValidEmail(email))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["A valid email address is required."] });
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            Email = email,
            UserName = email,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
        };

        // NpgsqlRetryingExecutionStrategy rejects user-initiated transactions, so the
        // whole transactional unit must run inside CreateExecutionStrategy().ExecuteAsync.
        // The strategy delegates the early-return IResult so the response logic is unchanged.
        var registrationOutcome = await db.Database.CreateExecutionStrategy().ExecuteAsync(
            async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                if (bootstrapRegistration && await db.AdminBootstraps.AnyAsync(cancellationToken))
                {
                    return Results.Problem(title: "Registration is closed.", statusCode: StatusCodes.Status403Forbidden);
                }

                var result = await userManager.CreateAsync(user, request.Password);
                if (!result.Succeeded)
                {
                    return Results.ValidationProblem(result.Errors
                        .GroupBy(error => error.Code)
                        .ToDictionary(group => group.Key, group => group.Select(error => error.Description).ToArray()));
                }
                var rolesSeeded = await EnsureCoreLifeRolesAsync(db, user.Id, cancellationToken);
                if (bootstrapRegistration)
                {
                    db.AdminBootstraps.Add(new AdminBootstrap(user.Id));
                }

                if (rolesSeeded || bootstrapRegistration)
                {
                    await db.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
                return (IResult?)null;
            });

        if (registrationOutcome is not null)
        {
            return registrationOutcome;
        }

        if (accountOptions.RequireConfirmedEmail)
        {
            try
            {
                var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
                await emailSender.SendEmailConfirmationAsync(user.Id, email, token, cancellationToken);
            }
            catch (Exception)
            {
                await CleanupBootstrapReservationAsync(db, userManager, user, cancellationToken);
                return Results.Problem(
                    title: "Account delivery is unavailable.",
                    detail: "The account could not be created because confirmation email delivery is unavailable.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }
        else
        {
            // Email confirmation is not enforced (SMTP not provisioned). Confirm the
            // account immediately so the user can sign in right away.
            if (!user.EmailConfirmed)
            {
                var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
                await userManager.ConfirmEmailAsync(user, confirmationToken);
            }

            if (bootstrapRegistration)
            {
                var completionFailure = await CompleteBootstrapAsync(db, userManager, roleManager, user, cancellationToken);
                if (completionFailure is not null)
                {
                    return completionFailure;
                }
            }
        }

        return Results.Accepted(
            "/api/v1/auth/me",
            new { emailConfirmationRequired = accountOptions.RequireConfirmedEmail });
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null || !user.IsActive)
        {
            return Results.Problem(title: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return Results.Problem(title: "Account is temporarily locked.", statusCode: StatusCodes.Status429TooManyRequests);
        }

        if (result.IsNotAllowed)
        {
            return Results.Problem(title: "Email confirmation is required.", statusCode: StatusCodes.Status403Forbidden);
        }

        return result.Succeeded
            ? Results.NoContent()
            : Results.Problem(title: "Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString("D", System.Globalization.CultureInfo.InvariantCulture));
        if (user is null)
        {
            return Results.NotFound();
        }

        if (!user.EmailConfirmed)
        {
            var result = await userManager.ConfirmEmailAsync(user, request.Token);
            if (!result.Succeeded)
            {
                return Results.ValidationProblem(result.Errors.ToDictionary(error => error.Code, error => new[] { error.Description }));
            }
        }

        var completionFailure = await CompleteBootstrapAsync(db, userManager, roleManager, user, cancellationToken);
        return completionFailure ?? Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUserAsync(ICurrentUser currentUser, UserManager<ApplicationUser> userManager)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(currentUser.UserId.ToString("D", System.Globalization.CultureInfo.InvariantCulture));
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Results.Ok(new AuthenticatedUserResponse(user.Id, user.Email ?? string.Empty, user.DisplayName, user.EmailConfirmed, roles.ToArray()));
    }

    private static bool BootstrapTokenMatches(string? suppliedToken, string tokenPath)
    {
        if (string.IsNullOrWhiteSpace(suppliedToken) || string.IsNullOrWhiteSpace(tokenPath))
        {
            return false;
        }

        try
        {
            var expectedToken = File.ReadAllText(tokenPath).Trim();
            if (expectedToken.Length < 24)
            {
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
            var suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken.Trim());
            return expectedBytes.Length == suppliedBytes.Length && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task CleanupBootstrapReservationAsync(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        CancellationToken cancellationToken)
    {        var reservation = await db.AdminBootstraps.SingleOrDefaultAsync(value => value.UserId == user.Id, cancellationToken);
        var seededRoles = await db.LifeRoles.Where(value => value.OwnerId == user.Id).ToListAsync(cancellationToken);
        if (reservation is not null && !reservation.CompletedAt.HasValue)
        {
            db.AdminBootstraps.Remove(reservation);
        }

        if (seededRoles.Count > 0)
        {
            db.LifeRoles.RemoveRange(seededRoles);
        }

        if (reservation is not null || seededRoles.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        await userManager.DeleteAsync(user);
    }

    private static async Task<IResult?> CompleteBootstrapAsync(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        var bootstrap = await db.AdminBootstraps.SingleOrDefaultAsync(cancellationToken);
        if (bootstrap is null || bootstrap.UserId != user.Id || bootstrap.CompletedAt.HasValue)
        {
            return null;
        }

        try
        {
            foreach (var roleName in new[] { "Admin", "KnowledgeOwner", "KnowledgeAdmin" })
            {
                if (await roleManager.RoleExistsAsync(roleName))
                {
                    continue;
                }

                var createRole = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName) { Id = Guid.CreateVersion7() });
                if (!createRole.Succeeded)
                {
                    return Results.Problem(title: "Administrator bootstrap is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }

            if (!await userManager.IsInRoleAsync(user, "Admin"))
            {
                var addRole = await userManager.AddToRoleAsync(user, "Admin");
                if (!addRole.Succeeded)
                {
                    return Results.Problem(title: "Administrator bootstrap is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }

            bootstrap.MarkCompleted();
            await db.SaveChangesAsync(cancellationToken);
            AddAudit(db, user.Id, "identity", "admin_bootstrap_completed", "user", user.Id);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException)
        {
            return Results.Problem(title: "Administrator bootstrap is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static void MapProjects(RouteGroupBuilder api)
    {
        var projects = api.MapGroup("/projects");
        projects.MapGet("", async (ProjectState? state, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken, bool includeArchived = false) =>
        {
            var query = db.Projects.AsNoTracking().Where(project => project.OwnerId == currentUser.UserId);
            if (state.HasValue)
            {
                query = query.Where(project => project.State == state.Value);
            }
            else if (!includeArchived)
            {
                query = query.Where(project => project.State != ProjectState.Archived);
            }

            var values = await query.OrderBy(project => project.SortOrder).ThenBy(project => project.Name).ToListAsync(cancellationToken);
            if (values.Count == 0)
            {
                return Results.Ok(Array.Empty<ProjectResponse>());
            }

            var projectIds = values.Select(project => project.Id).ToArray();
            var taskRows = await db.Tasks.AsNoTracking()
                .Where(task => task.OwnerId == currentUser.UserId && task.ProjectId.HasValue && projectIds.Contains(task.ProjectId.Value))
                .Select(task => new { ProjectId = task.ProjectId!.Value, task.State })
                .ToListAsync(cancellationToken);
            var counts = taskRows.GroupBy(task => task.ProjectId)
                .ToDictionary(
                    group => group.Key,
                    group => (Total: group.Count(), Completed: group.Count(task => task.State == TaskState.Completed)));
            return Results.Ok(values.Select(project => ToResponse(project, counts.GetValueOrDefault(project.Id))));
        });
        projects.MapPost("", async (CreateProjectRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            try
            {
                var project = new Project(
                    currentUser.UserId,
                    request.Name,
                    request.Description,
                    request.Color,
                    request.Icon,
                    request.StartAt,
                    request.TargetAt,
                    request.SortOrder);
                db.Projects.Add(project);
                AddAudit(db, currentUser.UserId, "project", "created", "project", project.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created("/api/v1/projects/" + project.Id, ToResponse(project, (0, 0)));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
        projects.MapGet("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(await ToResponseAsync(project, db, cancellationToken));
        });
        projects.MapPut("/{id:guid}", async (Guid id, UpdateProjectRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var project = await db.Projects.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            if (project.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The project was changed by another request. Refresh and retry." });
            }

            try
            {
                project.Update(request.Name, request.Description, request.Color, request.Icon, request.StartAt, request.TargetAt, request.SortOrder);
                if (project.State != request.State)
                {
                    switch (request.State)
                    {
                        case ProjectState.Completed:
                            project.Complete();
                            break;
                        case ProjectState.Archived:
                            project.Archive();
                            break;
                        case ProjectState.Active:
                            project.Restore();
                            break;
                    }
                }

                AddAudit(db, currentUser.UserId, "project", "updated", "project", project.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(await ToResponseAsync(project, db, cancellationToken));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
        projects.MapPost("/{id:guid}/archive", async (Guid id, ConcurrencyTokenRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var project = await db.Projects.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            if (project.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The project was changed by another request. Refresh and retry." });
            }

            project.Archive();
            AddAudit(db, currentUser.UserId, "project", "archived", "project", project.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToResponseAsync(project, db, cancellationToken));
        });
        projects.MapPost("/{id:guid}/restore", async (Guid id, ConcurrencyTokenRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var project = await db.Projects.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            if (project.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The project was changed by another request. Refresh and retry." });
            }

            project.Restore();
            AddAudit(db, currentUser.UserId, "project", "restored", "project", project.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(await ToResponseAsync(project, db, cancellationToken));
        });
        projects.MapPost("/{id:guid}/tasks/reorder", async (Guid id, ProjectTaskReorderRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId && value.State != ProjectState.Archived, cancellationToken);
            if (project is null)
            {
                return Results.NotFound();
            }

            var task = await db.Tasks.SingleOrDefaultAsync(value => value.Id == request.TaskId && value.OwnerId == currentUser.UserId && value.ProjectId == id, cancellationToken);
            if (task is null)
            {
                return Results.NotFound();
            }

            if (task.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The task was changed by another request. Refresh and retry." });
            }

            var siblings = await db.Tasks
                .Where(value => value.OwnerId == currentUser.UserId && value.ProjectId == id && value.State == request.State && value.Id != task.Id)
                .OrderBy(value => value.SortOrder)
                .ThenBy(value => value.Id)
                .ToListAsync(cancellationToken);
            var insertionIndex = siblings.Count;
            if (request.BeforeTaskId.HasValue)
            {
                insertionIndex = siblings.FindIndex(value => value.Id == request.BeforeTaskId.Value);
                if (insertionIndex < 0)
                {
                    return Results.BadRequest(new { error = "The requested placement target is not in this project column." });
                }
            }

            siblings.Insert(insertionIndex, task);
            for (var index = 0; index < siblings.Count; index++)
            {
                var value = siblings[index];
                value.Reposition(value.Id == task.Id ? request.State : value.State, (index + 1) * 1_000);
            }

            AddAudit(db, currentUser.UserId, "task", "reordered", "task", task.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(task));
        });
    }
    private static void MapTasks(RouteGroupBuilder api)
    {
        var tasks = api.MapGroup("/tasks");
        tasks.MapGet("", async (Guid? projectId, Guid? goalId, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var query = db.Tasks.AsNoTracking().Where(task => task.OwnerId == currentUser.UserId);
            if (projectId.HasValue)
            {
                query = query.Where(task => task.ProjectId == projectId.Value);
            }

            if (goalId.HasValue)
            {
                query = query.Where(task => task.GoalId == goalId.Value);
            }

            var values = await query
                .OrderBy(task => task.ProjectId)
                .ThenBy(task => task.State)
                .ThenBy(task => task.SortOrder)
                .ThenBy(task => task.DueAt)
                .ToListAsync(cancellationToken);
            return Results.Ok(values.Select(ToResponse));
        });
        tasks.MapPost("", async (CreateTaskRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            if (!await IsOwnedActiveProjectAsync(db, currentUser.UserId, request.ProjectId, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["projectId"] = ["The selected project does not exist or is archived."] });
            }


            if (!await IsOwnedActiveRoleAsync(db, currentUser.UserId, request.RoleId, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["roleId"] = ["The selected role does not exist or is archived."] });
            }

            if (!await IsOwnedActiveGoalAsync(db, currentUser.UserId, request.GoalId, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["goalId"] = ["The selected goal does not exist or is not active."] });
            }
            try
            {
                var sortOrder = request.SortOrder ?? await GetNextTaskSortOrderAsync(db, currentUser.UserId, request.ProjectId, request.State, cancellationToken);
                var task = new TaskItem(
                    currentUser.UserId,
                    request.Title,
                    request.Description,
                    request.LifeArea,
                    request.Quadrant,
                    request.DueAt,
                    request.EstimatedMinutes,
                    request.StartAt,
                    request.ProjectId,
                    sortOrder,
                    request.State,
                    request.RoleId,
                    request.GoalId);
                db.Tasks.Add(task);
                AddAudit(db, currentUser.UserId, "task", "created", "task", task.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created("/api/v1/tasks/" + task.Id, ToResponse(task));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
        tasks.MapPut("/{id:guid}", async (Guid id, UpdateTaskRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var task = await db.Tasks.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound();
            }

            if (task.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The task was changed by another request. Refresh and retry." });
            }

            if (!await IsOwnedActiveProjectAsync(db, currentUser.UserId, request.ProjectId, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["projectId"] = ["The selected project does not exist or is archived."] });
            }


            if (!await IsOwnedActiveRoleAsync(db, currentUser.UserId, request.RoleId, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["roleId"] = ["The selected role does not exist or is archived."] });
            }

            if (!await IsOwnedActiveGoalAsync(db, currentUser.UserId, request.GoalId, cancellationToken))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["goalId"] = ["The selected goal does not exist or is not active."] });
            }
            try
            {
                task.Update(
                    request.Title,
                    request.Description,
                    request.LifeArea,
                    request.Quadrant,
                    request.State,
                    request.StartAt,
                    request.DueAt,
                    request.EstimatedMinutes,
                    request.ReminderAt,
                    request.ProjectId,
                    request.SortOrder,
                    request.RoleId,
                    request.GoalId);
                await CancelPendingTaskClassificationProposalsAsync(
                    db,
                    currentUser.UserId,
                    task.Id,
                    cancellationToken);
                await CancelPendingTaskScheduleProposalsAsync(
                    db,
                    currentUser.UserId,
                    task.Id,
                    cancellationToken);
                AddAudit(db, currentUser.UserId, "task", "updated", "task", task.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToResponse(task));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
        tasks.MapDelete("/{id:guid}", async (Guid id, [FromBody] ConcurrencyTokenRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var task = await db.Tasks.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (task is null)
            {
                return Results.NotFound();
            }

            if (task.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The task was changed by another request. Refresh and retry." });
            }

            await CancelPendingTaskClassificationProposalsAsync(
                db,
                currentUser.UserId,
                task.Id,
                cancellationToken);
            await CancelPendingTaskScheduleProposalsAsync(
                db,
                currentUser.UserId,
                task.Id,
                cancellationToken);
            AddAudit(db, currentUser.UserId, "task", "deleted", "task", task.Id);
            db.Tasks.Remove(task);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }
    private static void MapCalendar(RouteGroupBuilder api)
    {
        var calendar = api.MapGroup("/calendar-events");
        calendar.MapGet("", async (DateTimeOffset? start, DateTimeOffset? end, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var query = db.CalendarEvents.AsNoTracking().Where(value => value.OwnerId == currentUser.UserId);
            if (start.HasValue)
            {
                query = query.Where(value => value.EndsAt >= start.Value);
            }

            if (end.HasValue)
            {
                query = query.Where(value => value.StartsAt <= end.Value);
            }

            var events = await query.OrderBy(value => value.StartsAt).ToListAsync(cancellationToken);
            return Results.Ok(events.Select(ToResponse));
        });
        calendar.MapPost("", async (CreateCalendarEventRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            try
            {
                var calendarEvent = new CalendarEvent(currentUser.UserId, request.Title, request.StartsAt, request.EndsAt, request.Color, request.Role);
                db.CalendarEvents.Add(calendarEvent);
                AddAudit(db, currentUser.UserId, "calendar", "created", "calendar_event", calendarEvent.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created("/api/v1/calendar-events/" + calendarEvent.Id, ToResponse(calendarEvent));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
        calendar.MapPut("/{id:guid}", async (Guid id, UpdateCalendarEventRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var calendarEvent = await db.CalendarEvents.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (calendarEvent is null)
            {
                return Results.NotFound();
            }

            if (calendarEvent.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The calendar event was changed by another request. Refresh and retry." });
            }

            try
            {
                calendarEvent.Update(request.Title, request.StartsAt, request.EndsAt, request.Color, request.Role);
                AddAudit(db, currentUser.UserId, "calendar", "updated", "calendar_event", calendarEvent.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToResponse(calendarEvent));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
        calendar.MapDelete("/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var calendarEvent = await db.CalendarEvents.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (calendarEvent is null)
            {
                return Results.NotFound();
            }

            AddAudit(db, currentUser.UserId, "calendar", "deleted", "calendar_event", calendarEvent.Id);
            db.CalendarEvents.Remove(calendarEvent);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static void MapHabits(RouteGroupBuilder api)
    {
        var habits = api.MapGroup("/habits");
        habits.MapGet("", async (AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken, bool includeArchived = false) =>
        {
            var query = db.Habits.AsNoTracking().Where(value => value.OwnerId == currentUser.UserId);
            if (!includeArchived)
            {
                query = query.Where(value => !value.IsArchived);
            }

            var values = await query.OrderBy(value => value.IsArchived).ThenBy(value => value.Title).ToListAsync(cancellationToken);
            return Results.Ok(values.Select(ToResponse));
        });
        habits.MapGet("/check-ins", async (DateOnly? from, DateOnly? to, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var query = db.HabitCheckIns.AsNoTracking().Where(value => value.OwnerId == currentUser.UserId);
            if (from.HasValue)
            {
                query = query.Where(value => value.OccurredOn >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(value => value.OccurredOn <= to.Value);
            }

            var values = await query.OrderBy(value => value.OccurredOn).ToListAsync(cancellationToken);
            return Results.Ok(values.Select(value => new HabitCheckInResponse(value.Id, value.HabitId, value.OccurredOn, value.Note)));
        });
        habits.MapPost("", async (CreateHabitRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            try
            {
                var habit = new Habit(currentUser.UserId, request.Title, request.Description, request.LifeArea, request.TargetPerWeek);
                db.Habits.Add(habit);
                AddAudit(db, currentUser.UserId, "habit", "created", "habit", habit.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created("/api/v1/habits/" + habit.Id, ToResponse(habit));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
        habits.MapPut("/{id:guid}", async (Guid id, UpdateHabitRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var habit = await db.Habits.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (habit is null)
            {
                return Results.NotFound();
            }

            if (habit.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The habit was changed by another request. Refresh and retry." });
            }

            try
            {
                habit.Update(request.Title, request.Description, request.LifeArea, request.TargetPerWeek);
                AddAudit(db, currentUser.UserId, "habit", "updated", "habit", habit.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToResponse(habit));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
        habits.MapDelete("/{id:guid}", async (Guid id, [FromBody] ConcurrencyTokenRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var habit = await db.Habits.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (habit is null)
            {
                return Results.NotFound();
            }

            if (habit.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The habit was changed by another request. Refresh and retry." });
            }

            habit.Archive();
            AddAudit(db, currentUser.UserId, "habit", "archived", "habit", habit.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(habit));
        });
        habits.MapPost("/{id:guid}/restore", async (Guid id, ConcurrencyTokenRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var habit = await db.Habits.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (habit is null)
            {
                return Results.NotFound();
            }

            if (habit.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The habit was changed by another request. Refresh and retry." });
            }

            habit.Restore();
            AddAudit(db, currentUser.UserId, "habit", "restored", "habit", habit.Id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(ToResponse(habit));
        });
        habits.MapPost("/{id:guid}/check-ins", async (Guid id, CreateHabitCheckInRequest request, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var habit = await db.Habits.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId && !value.IsArchived, cancellationToken);
            if (habit is null)
            {
                return Results.NotFound();
            }

            try
            {
                var checkIn = new HabitCheckIn(currentUser.UserId, habit.Id, request.OccurredOn, request.Note);
                db.HabitCheckIns.Add(checkIn);
                AddAudit(db, currentUser.UserId, "habit", "checked_in", "habit", habit.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created("/api/v1/habits/" + habit.Id + "/check-ins/" + checkIn.Id, new HabitCheckInResponse(checkIn.Id, checkIn.HabitId, checkIn.OccurredOn, checkIn.Note));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
            catch (DbUpdateException)
            {
                return Results.Conflict(new { error = "This habit is already checked in for that date." });
            }
        });
        habits.MapDelete("/{id:guid}/check-ins/{occurredOn}", async (Guid id, DateOnly occurredOn, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var checkIn = await db.HabitCheckIns.SingleOrDefaultAsync(value => value.HabitId == id && value.OwnerId == currentUser.UserId && value.OccurredOn == occurredOn, cancellationToken);
            if (checkIn is null)
            {
                return Results.NotFound();
            }

            db.HabitCheckIns.Remove(checkIn);
            AddAudit(db, currentUser.UserId, "habit", "check_in_removed", "habit", id);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }
    private static void MapJournal(RouteGroupBuilder api)
    {
        var journal = api.MapGroup("/journal");
        journal.MapGet("", async (AppDbContext db, ICurrentUser currentUser, IContentProtector protector, CancellationToken cancellationToken) =>
        {
            var entries = await db.JournalEntries.AsNoTracking()
                .Where(value => value.OwnerId == currentUser.UserId)
                .OrderByDescending(value => value.CreatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);
            var entryIds = entries.Select(entry => entry.Id).ToArray();
            var currentInsights = await db.JournalInsights.AsNoTracking()
                .Where(insight => insight.OwnerId == currentUser.UserId
                    && insight.State == JournalInsightState.Approved
                    && entryIds.Contains(insight.JournalEntryId))
                .ToListAsync(cancellationToken);
            var insightsByRevision = currentInsights.ToDictionary(
                insight => (insight.JournalEntryId, insight.SourceConcurrencyToken));
            return Results.Ok(entries.Select(value => ToResponse(
                value,
                currentUser.UserId,
                protector,
                insightsByRevision.GetValueOrDefault((value.Id, value.ConcurrencyToken)))));
        });
        journal.MapPost("", async (CreateJournalEntryRequest request, AppDbContext db, ICurrentUser currentUser, IContentProtector protector, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 20_000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["Journal content must be between 1 and 20,000 characters."] });
            }

            try
            {
                var entry = new JournalEntry(currentUser.UserId, protector.Protect(currentUser.UserId, request.Content), request.Mood);
                db.JournalEntries.Add(entry);
                AddAudit(db, currentUser.UserId, "journal", "created", "journal_entry", entry.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Created("/api/v1/journal/" + entry.Id, ToResponse(entry, currentUser.UserId, protector));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
        journal.MapPut("/{id:guid}", async (Guid id, UpdateJournalEntryRequest request, AppDbContext db, ICurrentUser currentUser, IContentProtector protector, CancellationToken cancellationToken) =>
        {
            var entry = await db.JournalEntries.SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            if (entry is null)
            {
                return Results.NotFound();
            }

            if (entry.ConcurrencyToken != request.ConcurrencyToken)
            {
                return Results.Conflict(new { error = "The journal entry was changed by another request. Refresh and retry." });
            }

            if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 20_000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["Journal content must be between 1 and 20,000 characters."] });
            }

            try
            {
                var priorRevision = entry.ConcurrencyToken;
                entry.ReplaceContent(protector.Protect(currentUser.UserId, request.Content), request.Mood);
                var pendingInsights = await db.JournalInsights
                    .Where(insight => insight.OwnerId == currentUser.UserId
                        && insight.JournalEntryId == entry.Id
                        && insight.SourceConcurrencyToken == priorRevision
                        && insight.State == JournalInsightState.Pending)
                    .ToListAsync(cancellationToken);
                foreach (var insight in pendingInsights)
                {
                    insight.Cancel();
                }

                var staleProposalKeys = pendingInsights
                    .Select(insight => JournalInsightProposalSourceKey(entry.Id, insight.SourceConcurrencyToken))
                    .ToArray();
                if (staleProposalKeys.Length > 0)
                {
                    var staleProposals = await db.AiActionProposals
                        .Where(proposal => proposal.OwnerId == currentUser.UserId
                            && proposal.Kind == AiProposalKind.ApplyJournalInsight
                            && proposal.State == AiProposalState.Pending
                            && staleProposalKeys.Contains(proposal.SourceKey))
                        .ToListAsync(cancellationToken);
                    foreach (var proposal in staleProposals)
                    {
                        proposal.Cancel();
                    }
                }

                AddAudit(db, currentUser.UserId, "journal", "updated", "journal_entry", entry.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Ok(ToResponse(entry, currentUser.UserId, protector));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
    }
    private static void MapDocuments(RouteGroupBuilder api)
    {
        var documents = api.MapGroup("/documents");
        documents.MapGet("", async (AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var values = await db.Documents.AsNoTracking()
                .Where(value => value.OwnerId == currentUser.UserId)
                .OrderByDescending(value => value.CreatedAt)
                .ToListAsync(cancellationToken);
            return Results.Ok(values.Select(ToResponse));
        });
        documents.MapPost("", async (CreateDocumentRequest request, AppDbContext db, ICurrentUser currentUser, IObjectStorage storage, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Content) || request.Content.Length > 1_000_000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["content"] = ["Document content must be between 1 and 1,000,000 characters."] });
            }

            try
            {
                var stored = await storage.PutTextAsync(
                    currentUser.UserId,
                    "source.txt",
                    request.Content,
                    request.ContentType,
                    cancellationToken);
                var document = new DocumentAsset(currentUser.UserId, request.Title, stored.ObjectKey, stored.ContentType, stored.SizeBytes);
                var roadmapName = string.IsNullOrWhiteSpace(request.RoadmapName)
                    ? "document-" + document.Id.ToString("N", System.Globalization.CultureInfo.InvariantCulture)
                    : request.RoadmapName.Trim();
                var job = new IngestionJob(currentUser.UserId, document.Id, roadmapName, request.SourceType);
                db.Documents.Add(document);
                db.IngestionJobs.Add(job);
                db.OutboxMessages.Add(new OutboxMessage(
                    currentUser.UserId,
                    "document.ingestion.requested",
                    JsonSerializer.Serialize(new IngestionOutboxEvent(job.Id, document.Id), JsonOptions)));
                AddAudit(db, currentUser.UserId, "document", "ingestion_queued", "document", document.Id);
                await db.SaveChangesAsync(cancellationToken);
                return Results.Accepted("/api/v1/jobs/" + job.Id, ToResponse(job));
            }
            catch (DomainValidationException exception)
            {
                return DomainValidationProblem(exception);
            }
        });
        api.MapGet("/jobs/{id:guid}", async (Guid id, AppDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            var job = await db.IngestionJobs.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id && value.OwnerId == currentUser.UserId, cancellationToken);
            return job is null ? Results.NotFound() : Results.Ok(ToResponse(job));
        });
    }

    private static void MapAi(RouteGroupBuilder api)
    {
        var ai = api.MapGroup("/ai");
        ai.MapPost("/query", async (GraphQueryRequest request, IAiGateway gateway, IKnowledgePlatformGateway knowledgePlatform, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > 4_000)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["question"] = ["Question must be between 1 and 4,000 characters."] });
            }

            var scope = request.Scope?.Trim().ToLowerInvariant() ?? "roadmap";
            if (scope is not ("roadmap" or "dataset"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["scope"] = ["Scope must be either 'roadmap' or 'dataset'."] });
            }

            string? knowledgeAccessGrant = null;
            if (scope == "dataset")
            {
                var selectedIds = request.DatasetIds?.Where(id => id != Guid.Empty).Distinct().OrderBy(id => id).ToArray() ?? [];
                if (selectedIds.Length == 0 || selectedIds.Length > 20)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["datasetIds"] = ["Select between one and twenty datasets before querying."] });
                }

                try
                {
                    knowledgeAccessGrant = (await knowledgePlatform.IssueAccessGrantAsync(currentUser.UserId, selectedIds, cancellationToken)).AccessGrant;
                }
                catch (HttpRequestException exception) when (exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                {
                    return Results.Problem(title: "One or more selected datasets are unavailable.", statusCode: StatusCodes.Status403Forbidden);
                }
                catch (HttpRequestException)
                {
                    return Results.Problem(title: "Knowledge access is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                request = request with { Scope = "dataset", DatasetIds = selectedIds };
            }
            else if (request.DatasetIds is { Count: > 0 })
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["datasetIds"] = ["Dataset IDs are valid only when scope is 'dataset'."] });
            }
            else
            {
                try
                {
                    var catalog = await knowledgePlatform.ListCatalogAsync(currentUser.UserId, cancellationToken);
                    var publishedIds = catalog
                        .Where(item => string.Equals(item.Visibility, "published", StringComparison.OrdinalIgnoreCase))
                        .Select(item => item.Id)
                        .Distinct()
                        .OrderBy(id => id)
                        .ToArray();

                    if (publishedIds.Length is > 0 and <= 20)
                    {
                        var grantResponse = await knowledgePlatform.IssueAccessGrantAsync(currentUser.UserId, publishedIds, cancellationToken);
                        knowledgeAccessGrant = grantResponse.AccessGrant;
                        request = request with { Scope = "dataset", DatasetIds = publishedIds };
                    }
                }
                catch (HttpRequestException)
                {
                    // Fall back gracefully to direct Neo4j roadmap GraphRAG query
                    request = request with { Scope = "roadmap" };
                }
            }
            try
            {
                return Results.Ok(await gateway.QueryGraphAsync(request, currentUser.UserId, knowledgeAccessGrant, cancellationToken));
            }
            catch (HttpRequestException)
            {
                return Results.Problem(title: "The AI service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });
        ai.MapPost("/balance", EvaluateBalanceAsync);
    }

    private static async Task<IResult> EvaluateBalanceAsync(
        AppDbContext db,
        IAiGateway gateway,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var windowEnd = DateTimeOffset.UtcNow;
        var windowStart = windowEnd.AddDays(-7);
        var activeRoles = await db.LifeRoles.AsNoTracking()
            .Where(role => role.OwnerId == currentUser.UserId && !role.IsArchived)
            .OrderBy(role => role.Category)
            .ThenBy(role => role.SortOrder)
            .ThenBy(role => role.Name)
            .ToListAsync(cancellationToken);
        if (activeRoles.Count == 0)
        {
            return Results.Conflict(new { error = "Create or restore at least one life role before evaluating balance." });
        }

        var activeRoleIds = activeRoles.Select(role => role.Id).ToHashSet();
        var completed = await db.Tasks.AsNoTracking()
            .Where(task => task.OwnerId == currentUser.UserId
                && task.State == TaskState.Completed
                && task.CompletedAt >= windowStart)
            .ToListAsync(cancellationToken);
        var completedByRole = completed
            .Where(task => task.RoleId.HasValue && activeRoleIds.Contains(task.RoleId.Value))
            .GroupBy(task => task.RoleId!.Value)
            .ToArray();
        var classifiedCount = completedByRole.Sum(group => group.Count());
        var workloads = completedByRole
            .Select(group => new Dictionary<string, object?>
            {
                ["role_id"] = group.Key,
                ["completed_task_count"] = group.Count(),
                ["completed_minutes"] = group.All(task => task.EstimatedMinutes.HasValue)
                    ? group.Sum(task => task.EstimatedMinutes!.Value)
                    : null,
            })
            .ToArray();
        var payload = new Dictionary<string, object?>
        {
            ["subject_id"] = currentUser.UserId,
            ["request_id"] = Guid.CreateVersion7(),
            ["window_start"] = windowStart,
            ["window_end"] = windowEnd,
            ["active_roles"] = activeRoles.Select(role => new
            {
                role_id = role.Id,
                role_name = role.Name,
                role_category = role.Category.ToString().ToLowerInvariant(),
                life_area = role.DefaultLifeArea.ToString().ToLowerInvariant(),
            }).ToArray(),
            ["role_workloads"] = workloads,
            ["unclassified_completed_task_count"] = completed.Count - classifiedCount,
            ["wellbeing_signal"] = "unknown",
        };

        try
        {
            using var result = await gateway.InvokeAsync("/api/balance/evaluate", payload, currentUser.UserId, "ai.invoke", cancellationToken);
            var root = result.RootElement;
            var suggestion = root.TryGetProperty("suggestion", out var suggestionValue) && suggestionValue.ValueKind == JsonValueKind.Object
                ? suggestionValue
                : default;
            var auditKey = root.GetProperty("audit_key").GetString() ?? Guid.CreateVersion7().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
            var risk = ParseRiskLevel(root.GetProperty("risk_level").GetString());
            var insight = root.GetProperty("insight").GetString() ?? string.Empty;
            var title = suggestion.ValueKind == JsonValueKind.Object && suggestion.TryGetProperty("title", out var titleValue) ? titleValue.GetString() : null;
            var description = suggestion.ValueKind == JsonValueKind.Object && suggestion.TryGetProperty("description", out var descriptionValue) ? descriptionValue.GetString() : null;
            var proposal = await CreateBalanceTaskProposalAsync(
                db,
                currentUser.UserId,
                auditKey,
                suggestion,
                cancellationToken);
            AddAudit(db, currentUser.UserId, "balance", "evaluated", "balance_evaluation", proposal?.Id);
            await db.SaveChangesAsync(cancellationToken);

            var dataQuality = root.TryGetProperty("data_quality", out var dataQualityValue) ? dataQualityValue.GetString() ?? "unknown" : "unknown";
            var attentionScore = root.TryGetProperty("attention_score", out var scoreValue) && scoreValue.TryGetInt32(out var score) ? score : 0;
            return Results.Ok(new BalanceEvaluationResponse(
                proposal?.Id,
                risk.ToString(),
                dataQuality,
                attentionScore,
                insight,
                title,
                description,
                proposal?.State == AiProposalState.Pending));
        }
        catch (HttpRequestException)
        {
            return Results.Problem(title: "The AI service is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { error = "A matching balance suggestion was created by another request. Refresh and review it." });
        }
    }
    private static void MapInternalJobs(IEndpointRouteBuilder app)
    {
        var internalApi = app.MapGroup("/internal/v1/jobs");
        internalApi.MapGet("/{id:guid}", async (
            Guid id,
            HttpRequest request,
            IServiceTokenValidator validator,
            IOptions<ServiceJwtOptions> jwt,
            AppDbContext db,
            IObjectStorage storage,
            CancellationToken cancellationToken) =>
        {
            var principal = ValidateInternalToken(request, validator, jwt.Value.ApiAudience, "jobs.process");
            if (!HasJobClaim(principal, id))
            {
                return Results.Unauthorized();
            }

            var job = await db.IngestionJobs.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
            if (job is null)
            {
                return Results.NotFound();
            }

            var document = await db.Documents.SingleOrDefaultAsync(value => value.Id == job.DocumentId && value.OwnerId == job.OwnerId, cancellationToken);
            if (document is null)
            {
                return Results.NotFound();
            }

            if (job.State is IngestionState.Queued or IngestionState.Failed)
            {
                job.MarkProcessing();
                document.MarkProcessing();
                await db.SaveChangesAsync(cancellationToken);
            }

            var content = await storage.GetTextAsync(document.ObjectKey, cancellationToken);
            return Results.Ok(new InternalIngestionJobResponse(job.Id, document.Id, job.RoadmapName, job.SourceType, content));
        });
        internalApi.MapPost("/{id:guid}/complete", async (
            Guid id,
            InternalIngestionCompletionRequest request,
            HttpRequest httpRequest,
            IServiceTokenValidator validator,
            IOptions<ServiceJwtOptions> jwt,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var principal = ValidateInternalToken(httpRequest, validator, jwt.Value.ApiAudience, "jobs.process");
            if (!HasJobClaim(principal, id))
            {
                return Results.Unauthorized();
            }

            var job = await db.IngestionJobs.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
            if (job is null)
            {
                return Results.NotFound();
            }

            var document = await db.Documents.SingleOrDefaultAsync(value => value.Id == job.DocumentId && value.OwnerId == job.OwnerId, cancellationToken);
            if (document is null)
            {
                return Results.NotFound();
            }

            if (request.Succeeded)
            {
                job.MarkSucceeded();
                document.MarkCompleted();
                AddAudit(db, job.OwnerId, "document", "ingestion_completed", "document", document.Id);
            }
            else
            {
                job.MarkFailed(request.Error ?? "AI worker reported an unspecified ingestion failure.");
                document.MarkFailed(request.Error ?? "AI worker reported an unspecified ingestion failure.");
                AddAudit(db, job.OwnerId, "document", "ingestion_failed", "document", document.Id);
            }

            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });
    }

    private static ClaimsPrincipal? ValidateInternalToken(HttpRequest request, IServiceTokenValidator validator, string audience, string scope)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header) ||
            !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(header.Parameter))
        {
            return null;
        }

        return validator.Validate(header.Parameter, audience, scope);
    }

    private static bool HasJobClaim(ClaimsPrincipal? principal, Guid jobId) =>
        principal?.FindFirst("job_id")?.Value == jobId.ToString("D", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<bool> IsOwnedActiveProjectAsync(AppDbContext db, Guid ownerId, Guid? projectId, CancellationToken cancellationToken)
    {
        return !projectId.HasValue || await db.Projects.AnyAsync(
            project => project.Id == projectId.Value && project.OwnerId == ownerId && project.State != ProjectState.Archived,
            cancellationToken);
    }

    private static async Task<int> GetNextTaskSortOrderAsync(
        AppDbContext db,
        Guid ownerId,
        Guid? projectId,
        TaskState state,
        CancellationToken cancellationToken)
    {
        var lastSortOrder = await db.Tasks
            .Where(task => task.OwnerId == ownerId && task.ProjectId == projectId && task.State == state)
            .Select(task => (int?)task.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;
        return lastSortOrder + 1_000;
    }

    private static async Task<ProjectResponse> ToResponseAsync(Project project, AppDbContext db, CancellationToken cancellationToken)
    {
        var states = await db.Tasks.AsNoTracking()
            .Where(task => task.OwnerId == project.OwnerId && task.ProjectId == project.Id)
            .Select(task => task.State)
            .ToListAsync(cancellationToken);
        return ToResponse(project, (states.Count, states.Count(state => state == TaskState.Completed)));
    }
    private static BalanceRiskLevel ParseRiskLevel(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "low" => BalanceRiskLevel.Low,
            "medium" => BalanceRiskLevel.Medium,
            "high" => BalanceRiskLevel.High,
            _ => BalanceRiskLevel.InsufficientData,
        };

    private static IResult DomainValidationProblem(DomainValidationException exception) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = [exception.Message] });

    private static void AddAudit(AppDbContext db, Guid? ownerId, string category, string action, string entityType, Guid? entityId) =>
        db.AuditEvents.Add(new AuditEvent(ownerId, category, action, entityType, entityId, "{}"));

    private static bool IsValidEmail(string email) =>
        email.Length is > 3 and <= 320 && email.Contains('@', StringComparison.Ordinal);

    private static TaskResponse ToResponse(TaskItem task) =>
        new(
            task.Id,
            task.Title,
            task.Description,
            task.LifeArea,
            task.Quadrant,
            task.State,
            task.ProjectId,
            task.RoleId,
            task.GoalId,
            task.StartAt,
            task.DueAt,
            task.ReminderAt,
            task.CompletedAt,
            task.EstimatedMinutes,
            task.SortOrder,
            task.ConcurrencyToken,
            task.CreatedAt,
            task.UpdatedAt);

    private static ProjectResponse ToResponse(Project project, (int Total, int Completed) counts)
    {
        var progress = counts.Total == 0 ? 0 : (int)Math.Round((double)counts.Completed * 100 / counts.Total, MidpointRounding.AwayFromZero);
        return new ProjectResponse(
            project.Id,
            project.Name,
            project.Description,
            project.Color,
            project.Icon,
            project.StartAt,
            project.TargetAt,
            project.State,
            project.SortOrder,
            counts.Total,
            counts.Completed,
            progress,
            project.ConcurrencyToken,
            project.CreatedAt,
            project.UpdatedAt);
    }

    private static JournalEntryResponse ToResponse(
        JournalEntry entry,
        Guid ownerId,
        IContentProtector protector,
        JournalInsight? insight = null) =>
        new(
            entry.Id,
            protector.Unprotect(ownerId, entry.ProtectedContent),
            entry.Mood,
            insight is null ? null : ToResponse(insight, ownerId, protector),
            entry.ConcurrencyToken,
            entry.CreatedAt,
            entry.UpdatedAt);

    private static CalendarEventResponse ToResponse(CalendarEvent calendarEvent) =>
        new(
            calendarEvent.Id,
            calendarEvent.Title,
            calendarEvent.StartsAt,
            calendarEvent.EndsAt,
            calendarEvent.Color,
            calendarEvent.Role,
            calendarEvent.ConcurrencyToken);

    private static HabitResponse ToResponse(Habit habit) =>
        new(habit.Id, habit.Title, habit.Description, habit.LifeArea, habit.TargetPerWeek, habit.IsArchived, habit.ConcurrencyToken);

    private static DocumentResponse ToResponse(DocumentAsset document) =>
        new(document.Id, document.Title, document.ContentType, document.SizeBytes, document.State, document.CreatedAt);

    private static JobResponse ToResponse(IngestionJob job) =>
        new(job.Id, job.DocumentId, job.State, job.AttemptCount, job.FailureReason, job.CreatedAt, job.UpdatedAt);
}
