using System;
using Microsoft.Azure.Cosmos;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos.Linq;

namespace nostify
{
    ///<summary>
    ///Base class to utilize nostify
    ///</summary>
    public class Nostify
    {

        ///<summary>
        ///Event store repository
        ///</summary>
        public NostifyCosmosClient Repository { get; }

        ///<summary>
        ///Default partition key
        ///</summary>
        public string DefaultPartitionKeyPath { get; }

        ///<summary>
        ///Default tenannt id value for use if NOT implementing multi-tenant
        ///</summary>
        public int DefaultTenantId { get; }
        

        ///<summary>
        ///Nostify constructor
        ///</summary>
        ///<param name="Primarykey">API Key value</param>
        ///<param name="DbName">Name of event store DB</param>
        ///<param name="EndpointUri">Uri of cosmos endpoint, format: https://[instance-name].documents.azure.us:443/</param>
        ///<param name="DefaultPartitionKeyPath">Path to partition key for default created current state container, set null to not create, leave default to partition by tenantId </param>
        ///<param name="DefaultTenantId">Default tenannt id value for use if NOT implementing multi-tenant, if left to default will create one partition in current state container</param>
        public Nostify(string Primarykey, string DbName, string EndpointUri, string DefaultPartitionKeyPath = "/tenantId", int DefaultTenantId = 0)
        {
            this.Repository = new NostifyCosmosClient(Primarykey, DbName, EndpointUri);
            if (DefaultPartitionKeyPath != null)
            {
                this.DefaultPartitionKeyPath = DefaultPartitionKeyPath;
            }
            this.DefaultTenantId = DefaultTenantId;
        }

        ///<summary>
        ///Nostify constructor, sets defaults for partition key path and default tenantId, do not use with multitenant.
        ///</summary>
        ///<param name="cosmosClient">Manually constructed client</param>
        public Nostify(NostifyCosmosClient cosmosClient)
        {
            this.Repository = cosmosClient;
            this.DefaultPartitionKeyPath = "/tenantId";
            this.DefaultTenantId = 0;
        }

    }
}
