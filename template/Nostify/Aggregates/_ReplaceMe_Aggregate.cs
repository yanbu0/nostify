using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using nostify;
using Newtonsoft.Json.Linq;


namespace _ReplaceMe__Service
{

    public class _ReplaceMe_Command : NostifyCommand
    {


        public _ReplaceMe_Command(string name)
        : base(name)
        {

        }
    }

    public class _ReplaceMe_ : Aggregate
    {
        public _ReplaceMe_()
        {
        }

        new public string aggregateType => "_ReplaceMe_";

        public override void Apply(PersistedEvent pe)
        {
            if (pe.command == NostifyCommand.Create || pe.command == NostifyCommand.Update)
            {
                this.UpdateProperties<_ReplaceMe_>(pe.payload);
            }
            else if (pe.command == NostifyCommand.Delete)
            {
                this.isDeleted = true;
            }
        }
    }


}
