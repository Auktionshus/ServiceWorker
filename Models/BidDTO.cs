using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

public class BidDTO
{
    public decimal Amount { get; set; }
    public Guid Bidder { get; set; }
    public Guid Auction { get; set; }
}
