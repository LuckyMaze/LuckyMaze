using System.Threading.Tasks;

namespace LuckyMaze.Infrastructure.Services
{
    /// <summary>
    /// Requests a privileged action on the Raspberry Pi host itself - shutting it down,
    /// switching wlan0 between the home WiFi and the cabinet's own hotspot - none of which the
    /// API container can do on its own. See scripts/hostagent/agent.sh, which actually performs
    /// these on the host side.
    /// </summary>
    public interface IHostAgentService
    {
        Task RequestShutdownAsync();
        Task RequestHotspotEnableAsync(bool permanent);
        Task RequestHotspotDisableAsync();
    }
}
