global using System;
global using System.IO;
global using System.Linq;
global using System.Net.Http;
global using System.Collections.Generic;
global using System.Threading;
global using System.Threading.Tasks;

namespace Microsoft.JSInterop
{
    public interface IJSRuntime
    {
        ValueTask<T> InvokeAsync<T>(string identifier, params object?[]? args);
    }
}

namespace Inventory.Client.Services
{
    public interface IAuthState { string? Token { get; } }
}
