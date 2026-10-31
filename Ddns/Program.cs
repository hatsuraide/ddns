using Ddns;
using DotNetEnv;

Env.Load();
return await CliApplication.Create().RunAsync(args);
