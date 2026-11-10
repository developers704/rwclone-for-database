namespace VallianiDataSync.Core.Interfaces;

public interface IAuthSession
{
    bool IsAuthenticated { get; }
    void MarkAuthenticated();
    void Lock();
}

public interface IOtpAuthService
{
    Task<(bool Ok, string Message)> SendLoginOtpsAsync(CancellationToken cancellationToken = default);
    Task<(bool Ok, string Message)> VerifyLoginOtpsAsync(string otpEmail1, string otpEmail2, CancellationToken cancellationToken = default);
    Task<(bool Ok, string Message)> SendSensitiveOtpsAsync(string reason, CancellationToken cancellationToken = default);
    Task<(bool Ok, string Message)> VerifySensitiveOtpsAsync(string otpEmail1, string otpEmail2, CancellationToken cancellationToken = default);
    (bool Ok, string Message) CheckIpAllowed();
}
