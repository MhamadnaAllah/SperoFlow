namespace SperoFlow.Application;

public interface IAccountEmailSender
{
    Task SendEmailConfirmationAsync(Guid userId, string email, string token, CancellationToken cancellationToken);

    Task SendPasswordResetAsync(Guid userId, string email, string token, CancellationToken cancellationToken);
}
