using UnityEngine;

namespace SavitGame.OS.Network {
    [System.Serializable]
    public class NetworkSettings {
        public string ipAddress = "192.168.1.100";
        public string subnetMask = "255.255.255.0";
        public string defaultGateway = "192.168.1.1";
        public string preferredDNS = "8.8.8.8";
        public string alternateDNS = "8.8.4.4";
        public bool useDHCP = false;
        
        public NetworkSettings() { }
        
        public NetworkSettings(NetworkSettings other) {
            if (other == null) {
                Debug.LogWarning("NetworkSettings: Tentando copiar de objeto nulo, usando valores padrão");
                return;
            }
            
            ipAddress = other.ipAddress;
            subnetMask = other.subnetMask;
            defaultGateway = other.defaultGateway;
            preferredDNS = other.preferredDNS;
            alternateDNS = other.alternateDNS;
            useDHCP = other.useDHCP;
        }
        
        public void LoadFromPlayerPrefs() {
            ipAddress = PlayerPrefs.GetString("OS_IP", "192.168.1.100");
            subnetMask = PlayerPrefs.GetString("OS_Subnet", "255.255.255.0");
            defaultGateway = PlayerPrefs.GetString("OS_Gateway", "192.168.1.1");
            preferredDNS = PlayerPrefs.GetString("OS_DNS1", "8.8.8.8");
            alternateDNS = PlayerPrefs.GetString("OS_DNS2", "8.8.4.4");
            useDHCP = PlayerPrefs.GetInt("OS_DHCP", 0) == 1;
        }
        
        public void SaveToPlayerPrefs() {
            PlayerPrefs.SetString("OS_IP", ipAddress);
            PlayerPrefs.SetString("OS_Subnet", subnetMask);
            PlayerPrefs.SetString("OS_Gateway", defaultGateway);
            PlayerPrefs.SetString("OS_DNS1", preferredDNS);
            PlayerPrefs.SetString("OS_DNS2", alternateDNS);
            PlayerPrefs.SetInt("OS_DHCP", useDHCP ? 1 : 0);
            PlayerPrefs.Save();
        }
        
        public bool ValidateAll() {
            return ValidateIPAddress(ipAddress) &&
                   ValidateIPAddress(subnetMask) &&
                   ValidateIPAddress(defaultGateway) &&
                   ValidateIPAddress(preferredDNS) &&
                   (string.IsNullOrEmpty(alternateDNS) || ValidateIPAddress(alternateDNS));
        }
        
        public static bool ValidateIPAddress(string ip) {
            if (string.IsNullOrEmpty(ip)) return false;
            
            string[] octets = ip.Split('.');
            if (octets.Length != 4) return false;
            
            foreach (string octet in octets) {
                if (!int.TryParse(octet, out int value) || value < 0 || value > 255)
                    return false;
            }
            return true;
        }
        
        public override string ToString() {
            return $"IP: {ipAddress}\n" +
                   $"Subnet: {subnetMask}\n" +
                   $"Gateway: {defaultGateway}\n" +
                   $"DNS1: {preferredDNS}\n" +
                   $"DNS2: {alternateDNS}\n" +
                   $"DHCP: {(useDHCP ? "Enabled" : "Disabled")}";
        }
    }
}