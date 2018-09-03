using System;
using System.Collections.Generic;
using System.Text;

namespace BackupArm
{
    class ServicePrincipal
    {
        public string FriendlyName { get; set; }
        public string TenantId { get; set; }
        public string SubscriptionId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string AccessToken { get; set; }
    }
}
