using HotelManagement.Application.Auth;
using HotelManagement.Application.Common;
using HotelManagement.Application.Contracts;
using HotelManagement.Application.Users;
using HotelManagement.Domain;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using static HotelManagement.UnitTests.TestData;

namespace HotelManagement.UnitTests.Application;

public class AuthServiceTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IAuthSessionService _sessions = Substitute.For<IAuthSessionService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly AuthService _sut;
    private readonly User _user = new(CompanyId, "a@x.com", "Alice", UserRole.Admin, "stored-hash");

    public AuthServiceTests()
    {
        _hasher.Hash(default!).ReturnsForAnyArgs("hashed");
        _sessions.StartSession(default!).ReturnsForAnyArgs(call =>
            new AuthResponse("access", Now, "refresh", Now.AddDays(14), UserResponse.From(call.Arg<User>())));
        _users.FindByEmailAsync("a@x.com", Arg.Any<CancellationToken>()).Returns(_user);
        _sut = new AuthService(Substitute.For<ICompanyRepository>(), _users, _unitOfWork, _hasher, new PasswordPolicy(),
            _sessions, Substitute.For<ICurrentUser>(), Options.Create(new LoginSecurityOptions()), _clock);
    }

    private Task<AuthResponse> Login(string password, string email = "A@X.com") =>
        _sut.LoginAsync(new LoginRequest { Email = email, Password = password }, default);

    private void PasswordIs(string password, PasswordCheck result = PasswordCheck.Success) =>
        _hasher.Verify("stored-hash", password).Returns(result);

    [Fact]
    public async Task Successful_login_starts_a_session()
    {
        PasswordIs("right");

        var result = await Login("right", email: "  A@X.COM "); // email is normalized before lookup

        Assert.Equal("access", result.AccessToken);
        Assert.Equal("refresh", result.RefreshToken);
        _sessions.Received(1).StartSession(_user);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_email_still_runs_a_hash_check()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Login("whatever", email: "nobody@x.com"));

        Assert.Equal(ErrorKind.Unauthorized, ex.Kind);
        Assert.Equal(AuthService.LoginFailedMessage, ex.Message);
        _hasher.Received(1).Verify(null, "whatever"); // same cost as a real check: no timing difference
    }

    [Fact]
    public async Task Wrong_password_counts_a_failed_attempt()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => Login("wrong"));

        Assert.Equal(AuthService.LoginFailedMessage, ex.Message);
        Assert.Equal(1, _user.FailedLoginCount);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _sessions.DidNotReceiveWithAnyArgs().StartSession(default!);
    }

    [Fact]
    public async Task Five_failures_lock_the_account_even_for_the_right_password()
    {
        PasswordIs("right");
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAsync<DomainException>(() => Login("wrong"));

        Assert.True(_user.IsLockedOut(Now));
        var locked = await Assert.ThrowsAsync<DomainException>(() => Login("right"));
        Assert.Equal(AuthService.LoginFailedMessage, locked.Message); // same message: no account-state leak

        _clock.Advance(TimeSpan.FromMinutes(16));
        var result = await Login("right");
        Assert.Equal("access", result.AccessToken);
        Assert.False(_user.IsLockedOut(_clock.GetUtcNow().UtcDateTime));
    }

    [Fact]
    public async Task Deactivated_user_cannot_log_in_and_gets_the_generic_message()
    {
        PasswordIs("right");
        _user.Update("Alice", UserRole.Admin, isActive: false, changedBy: Guid.NewGuid());

        var ex = await Assert.ThrowsAsync<DomainException>(() => Login("right"));
        Assert.Equal(AuthService.LoginFailedMessage, ex.Message);
    }

    [Fact]
    public async Task Outdated_hash_is_upgraded_on_successful_login()
    {
        PasswordIs("right", PasswordCheck.SuccessRehashNeeded);
        _hasher.Hash("right").Returns("upgraded-hash");

        await Login("right");

        Assert.Equal("upgraded-hash", _user.PasswordHash);
    }

    [Fact]
    public async Task Successful_login_resets_the_failure_counter()
    {
        PasswordIs("right");
        await Assert.ThrowsAsync<DomainException>(() => Login("wrong"));
        await Login("right");
        Assert.Equal(0, _user.FailedLoginCount);
    }

    [Fact]
    public async Task Register_rejects_weak_passwords()
    {
        var ex = await Assert.ThrowsAsync<DomainException>(() => _sut.RegisterCompanyAsync(new RegisterCompanyRequest
        {
            CompanyName = "Co", FullName = "Owner", Email = "owner@x.com", Password = "password123",
        }, default));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
    }

    [Fact]
    public async Task Register_with_taken_email_gives_a_non_revealing_conflict()
    {
        _users.EmailExistsAsync("taken@x.com", Arg.Any<CancellationToken>()).Returns(true);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _sut.RegisterCompanyAsync(new RegisterCompanyRequest
        {
            CompanyName = "Co", FullName = "Name", Email = "Taken@x.com", Password = "Correct-Horse-Battery",
        }, default));

        Assert.Equal(ErrorKind.Conflict, ex.Kind);
        Assert.Equal(AuthService.EmailUnavailableMessage, ex.Message);
        _users.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Register_creates_an_admin_with_hashed_password_and_a_session()
    {
        var result = await _sut.RegisterCompanyAsync(new RegisterCompanyRequest
        {
            CompanyName = "Co", FullName = "Owner", Email = "owner@x.com", Password = "Correct-Horse-Battery",
        }, default);

        _users.Received(1).Add(Arg.Is<User>(u => u.Role == UserRole.Admin && u.PasswordHash == "hashed"));
        Assert.Equal("refresh", result.RefreshToken);
    }
}

public class AuthSessionServiceTests
{
    private readonly IRefreshTokenRepository _tokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ISecureTokenGenerator _generator = Substitute.For<ISecureTokenGenerator>();
    private readonly ITokenService _accessTokens = Substitute.For<ITokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly AuthSessionService _sut;
    private readonly User _user = new(CompanyId, "a@x.com", "Alice", UserRole.Admin, "hash");

    public AuthSessionServiceTests()
    {
        var counter = 0;
        _generator.NewToken().Returns(_ => $"token-{++counter}");
        _generator.Hash(default!).ReturnsForAnyArgs(call => $"hash({call.Arg<string>()})");
        _accessTokens.CreateToken(default!).ReturnsForAnyArgs(new AccessToken("access", Now.AddMinutes(15)));
        _users.FindByIdAcrossTenantsAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        _sut = new AuthSessionService(_tokens, _users, _generator, _accessTokens, _unitOfWork,
            Options.Create(new RefreshTokenOptions()), _clock);
    }

    private RefreshToken Stored(string plain, Guid family) => new(_user, $"hash({plain})", family, Now, TimeSpan.FromDays(14));

    [Fact]
    public void New_session_stores_only_the_hash_of_the_refresh_token()
    {
        var response = _sut.StartSession(_user);

        Assert.Equal("token-1", response.RefreshToken);
        Assert.Equal(Now.AddDays(14), response.RefreshTokenExpiresAt);
        _tokens.Received(1).Add(Arg.Is<RefreshToken>(t => t.TokenHash == "hash(token-1)" && t.UserId == _user.Id));
    }

    [Fact]
    public async Task Refresh_rotates_the_token_within_the_same_family()
    {
        var family = Guid.NewGuid();
        var old = Stored("old", family);
        _tokens.FindByHashAsync("hash(old)", Arg.Any<CancellationToken>()).Returns(old);

        var response = await _sut.RefreshAsync("old", default);

        Assert.Equal("token-1", response.RefreshToken);
        Assert.True(old.IsRevoked);
        Assert.Equal("Rotated", old.RevocationReason);
        _tokens.Received(1).Add(Arg.Is<RefreshToken>(t => t.FamilyId == family && t.Id == old.ReplacedByTokenId));
    }

    [Fact]
    public async Task Reusing_a_rotated_token_revokes_the_whole_session()
    {
        var family = Guid.NewGuid();
        var used = Stored("used", family);
        used.Revoke(Now, "Rotated");
        var current = Stored("current", family);
        _tokens.FindByHashAsync("hash(used)", Arg.Any<CancellationToken>()).Returns(used);
        _tokens.ListActiveInFamilyAsync(family, Arg.Any<CancellationToken>()).Returns([current]);

        var ex = await Assert.ThrowsAsync<DomainException>(() => _sut.RefreshAsync("used", default));

        Assert.Equal(ErrorKind.Unauthorized, ex.Kind);
        Assert.True(current.IsRevoked); // the attacker's (or victim's) newer token is dead too
        Assert.Equal("Refresh token reuse detected", current.RevocationReason);
    }

    [Fact]
    public async Task Expired_or_unknown_tokens_are_rejected()
    {
        var expired = Stored("expired", Guid.NewGuid());
        _tokens.FindByHashAsync("hash(expired)", Arg.Any<CancellationToken>()).Returns(expired);
        _clock.Advance(TimeSpan.FromDays(15));

        await Assert.ThrowsAsync<DomainException>(() => _sut.RefreshAsync("expired", default));
        await Assert.ThrowsAsync<DomainException>(() => _sut.RefreshAsync("never-issued", default));
    }

    [Fact]
    public async Task Deactivated_user_cannot_refresh()
    {
        _user.Update("Alice", UserRole.Admin, isActive: false, changedBy: Guid.NewGuid());
        var token = Stored("t", Guid.NewGuid());
        _tokens.FindByHashAsync("hash(t)", Arg.Any<CancellationToken>()).Returns(token);

        await Assert.ThrowsAsync<DomainException>(() => _sut.RefreshAsync("t", default));
        Assert.True(token.IsRevoked);
    }

    [Fact]
    public async Task Logout_revokes_the_session_family()
    {
        var family = Guid.NewGuid();
        var token = Stored("t", family);
        _tokens.FindByHashAsync("hash(t)", Arg.Any<CancellationToken>()).Returns(token);
        _tokens.ListActiveInFamilyAsync(family, Arg.Any<CancellationToken>()).Returns([token]);

        await _sut.LogoutAsync("t", default);

        Assert.Equal("Logged out", token.RevocationReason);
    }
}

public class SessionValidatorTests
{
    [Fact]
    public async Task Valid_only_for_active_users_with_the_current_stamp()
    {
        var users = Substitute.For<IUserRepository>();
        var id = Guid.NewGuid();
        users.GetSessionStateAsync(id, Arg.Any<CancellationToken>()).Returns(new UserSessionState(true, "stamp-1"));
        var sut = new SessionValidator(users);

        Assert.True(await sut.IsValidAsync(id, "stamp-1", default));
        Assert.False(await sut.IsValidAsync(id, "stamp-0", default));   // role changed / deactivated since issue
        Assert.False(await sut.IsValidAsync(id, null, default));        // token from before stamps existed
        Assert.False(await sut.IsValidAsync(Guid.NewGuid(), "stamp-1", default)); // user gone

        users.GetSessionStateAsync(id, Arg.Any<CancellationToken>()).Returns(new UserSessionState(false, "stamp-1"));
        Assert.False(await sut.IsValidAsync(id, "stamp-1", default));
    }
}

public class PasswordPolicyTests
{
    private readonly PasswordPolicy _policy = new();

    [Theory]
    [InlineData("Correct-Horse-Battery")]
    [InlineData("my hotel has 12 rooms")]
    [InlineData("Tr0ub4dor&3xyz")]
    public void Accepts_reasonable_passwords(string password) =>
        _policy.EnsureAcceptable(password, "owner@example.com", "Olivia Owner");

    [Theory]
    [InlineData("short1!", "at least 10")]
    [InlineData("password123", "too common")]
    [InlineData("Passw0rd!!!", "too common")]
    [InlineData("qwertyuiop", "too common")]
    [InlineData("aaaaaaaaaaaa", "repetitive")]
    [InlineData("abababababab", "repetitive")]
    [InlineData("1234567890", "sequence")]
    [InlineData("olivia-secure-2026", "email address or name")]
    [InlineData("owner-is-me-2026", "email address or name")]
    public void Rejects_weak_passwords(string password, string reason)
    {
        var ex = Assert.Throws<DomainException>(() => _policy.EnsureAcceptable(password, "owner@example.com", "Olivia Owner"));
        Assert.Equal(ErrorKind.Validation, ex.Kind);
        Assert.Contains(reason, ex.Message);
    }

    [Fact]
    public void Rejects_overlong_passwords() =>
        Assert.Throws<DomainException>(() => _policy.EnsureAcceptable(new string('x', 120) + "Abcdefgh12", "a@b.c"));
}
