using Soenneker.Instagram.Accounts.Abstract;
using Soenneker.Tests.HostedUnit;

namespace Soenneker.Instagram.Accounts.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class InstagramAccountsUtilTests : HostedUnitTest
{
    private readonly IInstagramAccountsUtil _util;

    public InstagramAccountsUtilTests(Host host) : base(host)
    {
        _util = Resolve<IInstagramAccountsUtil>(true);
    }

    [Test]
    public void Registrar_resolves_accounts_and_client_dependencies()
    {
        if (_util is not InstagramAccountsUtil)
            throw new System.InvalidOperationException("The registrar did not resolve the account utility.");
    }
}
