using Microsoft.Extensions.Hosting;
using VpsOperator.Mcp;

var host = OperatorHost.Create(args);
await host.RunAsync().ConfigureAwait(false);

public partial class Program;
