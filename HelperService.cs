using System.Diagnostics;
using System.Security.Cryptography;
using System.ServiceProcess;

namespace ZephyrRemote;

public class HelperService : ServiceBase
{
    public const string Name = "ZephyrRemoteHelper";

    private volatile bool running;
    private Process? agent;
    private uint agentSession = uint.MaxValue;
    private string key = "";

    public HelperService()
    {
        ServiceName = Name;
    }

    protected override void OnStart(string[] args)
    {
        key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        AgentKey.Write(key);
        running = true;
        new Thread(Watch) { IsBackground = true }.Start();
    }

    protected override void OnStop()
    {
        running = false;
        StopAgent();
    }

    private void Watch()
    {
        while (running)
        {
            uint session = SessionLauncher.WTSGetActiveConsoleSessionId();
            bool dead = agent == null || agent.HasExited;
            if (session != 0xFFFFFFFF && (dead || session != agentSession))
            {
                StopAgent();
                agent = SessionLauncher.Start(Environment.ProcessPath!, $"--agent {key}", session);
                agentSession = session;
            }
            Thread.Sleep(2000);
        }
    }

    private void StopAgent()
    {
        if (agent != null && !agent.HasExited)
            agent.Kill();
        agent = null;
    }
}
