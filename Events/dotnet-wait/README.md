# dotnet-wait
CLI tool to monitor waits duration for .NET 9+ applications.

Install it with the following command line: **dotnet tool install -g dotnet-wait**

- To monitor a running .NET application, use the following command line with its process ID: **dotnet wait -p 1234**
- If you want to avoid mixing console output, use **-o** option to redirect the output to a file: **dotnet wait -p 1234 -o output.txt**
- If you want to avoid mixing console input, you can open 2 consoles. Type **dotnet wait -s -- "C:\path\to\my\app.exe"** in the first console and will get the command line **dotnet wait -r <pid>** to type in the second console
- use **-w <duration in milliseconds>** to monitor only waits longer than the duration specified



