using System;
using System.Collections.Generic;
using System.Text;

namespace ELO.Models
{
    public class PlayerHistory
    {
        public ulong UserId { get; set; }
        
        public ulong GameId { get; set; }

        public int Rank { get; set; }



    }
}
