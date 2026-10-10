using Inventory.Shared;
using Xunit;

namespace MoadianPayload.Tests;

/// <summary>
/// Unit tests for the Moadian buyer-field mapping and validation (Tob / Bid / Tinb).
///
/// Owner-confirmed wire rules (1405/07/17):
///   Tob = 1 -> Individual  : Bid must be a 10-digit national ID with a valid control digit; Tinb must be empty
///   Tob = 2 -> Legal       : Bid (if present) must be an 11-digit national ID with a valid control digit;
///                            Tinb (if present) must be exactly 14 digits (economic code);
///                            at least one of (valid Bid) or (valid Tinb) is required
///
/// ERP sends a single buyer id; the type is derived from its length:
///   10 digits -> Tob=1, Bid=id
///   11 digits -> Tob=2, Bid=id, Tinb=id  (sample verified in the system: legal buyer sends the
///   11-digit national ID in BOTH Bid and Tinb - this fixed the 00012 "economic number empty" error)
///   14 digits -> Tob=2, Tinb=id
///
/// Verified test data:
///   14014038299   -> valid 11-digit national ID (legal), control digit OK (680 % 11 == 9)
///   1234567898    -> valid 10-digit national ID (individual), control digit OK (1031 % 11 == 8)
///   14014038290   -> 11 digits, WRONG control digit
///   1234567890    -> 10 digits, WRONG control digit
///   12345678901234-> 14-digit economic code (pattern only matters)
///   30000000000   -> 11 digits where sum % 11 == 10 and control == 0 (the r==10 rule)
/// </summary>
public class ResolveBuyerTests
{
    [Fact]
    public void Ten_Digits_Maps_To_Individual_Bid()
    {
        var (tob, bid, tinb) = MoadianBuyerValidator.ResolveBuyer("1234567898");
        Assert.Equal((int)MoadianBuyerType.Individual, tob); // Tob = 1
        Assert.Equal(1, tob);
        Assert.Equal("1234567898", bid);
        Assert.Null(tinb);
    }

    [Fact]
    public void Eleven_Digits_Maps_To_Legal_Bid_And_Tinb()
    {
        var (tob, bid, tinb) = MoadianBuyerValidator.ResolveBuyer("14014038299");
        Assert.Equal((int)MoadianBuyerType.Legal, tob); // Tob = 2
        Assert.Equal(2, tob);
        Assert.Equal("14014038299", bid);
        Assert.Equal("14014038299", tinb); // same 11-digit national ID in Tinb (system-verified sample)
    }

    [Fact]
    public void Fourteen_Digits_Maps_To_Legal_Tinb()
    {
        var (tob, bid, tinb) = MoadianBuyerValidator.ResolveBuyer("12345678901234");
        Assert.Equal((int)MoadianBuyerType.Legal, tob); // Tob = 2
        Assert.Equal(2, tob);
        Assert.Null(bid);
        Assert.Equal("12345678901234", tinb);
    }

    [Theory]
    [InlineData("123456789")]    // 9 digits
    [InlineData("123456789012")] // 12 digits
    [InlineData("")]
    [InlineData("   ")]
    public void Unsupported_Lengths_Are_Unmapped(string id)
    {
        var (tob, bid, tinb) = MoadianBuyerValidator.ResolveBuyer(id);
        Assert.Null(tob);
        Assert.Null(bid);
        Assert.Null(tinb);
    }

    [Fact]
    public void Null_Is_Unmapped()
    {
        var (tob, bid, tinb) = MoadianBuyerValidator.ResolveBuyer(null);
        Assert.Null(tob);
        Assert.Null(bid);
        Assert.Null(tinb);
    }

    [Fact]
    public void Non_Digits_Are_Unmapped()
    {
        var (tob, bid, tinb) = MoadianBuyerValidator.ResolveBuyer("1401403829A");
        Assert.Null(tob);
        Assert.Null(bid);
        Assert.Null(tinb);
    }
}

public class ValidateTests
{
    /// <summary>
    /// An 11-digit Tinb with a wrong control digit must be rejected with field "tinb".
    /// (A VALID 11-digit national ID in Tinb is now accepted - system-verified sample.)
    /// </summary>
    [Fact]
    public void Eleven_Digit_Tinb_With_Wrong_Control_Digit_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: null, tinb: "14014038290");
        Assert.Contains(issues, i => i.Field == "tinb");
    }

    /// <summary>Owner case: Tob=2 (legal) with 14014038299 in Bid and Tinb=null must be accepted.</summary>
    [Fact]
    public void Tob2_Legal_Bid_14014038299_TinbNull_Is_Accepted()
    {
        Assert.Empty(MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: "14014038299", tinb: null));
    }

    /// <summary>The exact system-verified sample: Tob=2, Bid=Tinb=11-digit national ID must be accepted.</summary>
    [Fact]
    public void Tob2_Sample_BidAndTinb_Same_Eleven_Digit_Is_Accepted()
    {
        Assert.Empty(MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: "14014038299", tinb: "14014038299"));
    }

    [Fact]
    public void Tob2_Tinb_Eleven_Digit_NationalId_Without_Bid_Is_Accepted()
    {
        Assert.Empty(MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: null, tinb: "14014038299"));
    }

    [Fact]
    public void Tob2_Tinb_Eleven_Digit_With_Wrong_Control_Digit_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: "14014038299", tinb: "14014038290");
        Assert.Contains(issues, i => i.Field == "tinb");
    }

    [Fact]
    public void Tob2_Tinb_Ten_Digit_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: "14014038299", tinb: "1234567898");
        Assert.Contains(issues, i => i.Field == "tinb");
    }

    [Fact]
    public void Fourteen_Digit_Tinb_Is_Accepted()
    {
        Assert.Empty(MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: null, tinb: "12345678901234"));
    }

    [Fact]
    public void Ten_Digit_Individual_Bid_Is_Accepted()
    {
        Assert.Empty(MoadianBuyerValidator.Validate((int)MoadianBuyerType.Individual, bid: "1234567898", tinb: null));
    }

    [Fact]
    public void Legal_With_Both_Bid_And_Tinb_Is_Accepted()
    {
        Assert.Empty(MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: "14014038299", tinb: "12345678901234"));
    }

    /// <summary>Owner case: Tob=1 (individual) with an 11-digit id must be rejected.</summary>
    [Fact]
    public void Tob1_Individual_With_Eleven_Digit_Bid_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Individual, bid: "14014038299", tinb: null);
        Assert.Contains(issues, i => i.Field == "bid");
    }

    [Fact]
    public void Eleven_Digit_Bid_With_Wrong_Control_Digit_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: "14014038290", tinb: null);
        Assert.Contains(issues, i => i.Field == "bid");
    }

    [Fact]
    public void Ten_Digit_Bid_With_Wrong_Control_Digit_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Individual, bid: "1234567890", tinb: null);
        Assert.Contains(issues, i => i.Field == "bid");
    }

    [Fact]
    public void Individual_With_No_Bid_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Individual, bid: null, tinb: null);
        Assert.Contains(issues, i => i.Field == "bid");
    }

    [Fact]
    public void Individual_With_NonEmpty_Tinb_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Individual, bid: "1234567898", tinb: "12345678901234");
        Assert.Contains(issues, i => i.Field == "tinb");
    }

    [Fact]
    public void Legal_With_Neither_Bid_Nor_Tinb_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: null, tinb: null);
        Assert.Contains(issues, i => i.Field == "bid");
    }

    [Fact]
    public void Twelve_Digit_Bid_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: "140140382991", tinb: null);
        Assert.Contains(issues, i => i.Field == "bid");
    }

    [Theory]
    [InlineData("1234567890123")]   // 13 digits
    [InlineData("123456789012")]    // 12 digits
    [InlineData("123456789012345")] // 15 digits
    public void Tinb_Wrong_Length_Is_Rejected(string tinb)
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: "14014038299", tinb: tinb);
        Assert.Contains(issues, i => i.Field == "tinb");
    }

    [Fact]
    public void Tinb_With_Letters_Is_Rejected()
    {
        var issues = MoadianBuyerValidator.Validate((int)MoadianBuyerType.Legal, bid: "14014038299", tinb: "1234567890123A");
        Assert.Contains(issues, i => i.Field == "tinb");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(42)]
    public void Tob_Outside_Allowed_Enum_Is_Rejected(int tob)
    {
        var issues = MoadianBuyerValidator.Validate(tob, bid: "14014038299", tinb: null);
        Assert.Single(issues);
        Assert.Equal("tob", issues[0].Field);
    }
}

public class EnsureValidTests
{
    [Fact]
    public void Eleven_Digit_Legal_Passes()
    {
        MoadianBuyerValidator.EnsureValid("14014038299"); // resolves to Tob=2 legal, Bid+Tinb = 11-digit ID -> must not throw
    }

    [Fact]
    public void Fourteen_Digit_Economic_Code_Passes()
    {
        MoadianBuyerValidator.EnsureValid("12345678901234"); // resolves to Tob=2 legal, Tinb -> must not throw
    }

    [Fact]
    public void Ten_Digit_Individual_Passes()
    {
        MoadianBuyerValidator.EnsureValid("1234567898"); // resolves to Tob=1 individual, Bid -> must not throw
    }

    [Fact]
    public void Wrong_Control_Digit_Throws_For_Bid_And_Tinb()
    {
        var ex = Assert.Throws<MoadianBuyerValidationException>(() => MoadianBuyerValidator.EnsureValid("14014038290"));
        Assert.Contains("bid", ex.Field);
        Assert.Contains("tinb", ex.Field); // 11-digit ID now lands in BOTH bid and tinb
        Assert.Contains("bid", ex.Message);
    }

    [Fact]
    public void Empty_Throws_With_Field_Tob()
    {
        var ex = Assert.Throws<MoadianBuyerValidationException>(() => MoadianBuyerValidator.EnsureValid(""));
        Assert.Equal("tob", ex.Field);
    }

    [Fact]
    public void Null_Throws_With_Field_Tob()
    {
        var ex = Assert.Throws<MoadianBuyerValidationException>(() => MoadianBuyerValidator.EnsureValid(null));
        Assert.Equal("tob", ex.Field);
    }

    [Fact]
    public void Unsupported_Length_Throws()
    {
        var ex = Assert.Throws<MoadianBuyerValidationException>(() => MoadianBuyerValidator.EnsureValid("123456789"));
        Assert.Equal("tob", ex.Field);
    }
}

public class ControlDigitTests
{
    [Fact]
    public void Known_Valid_Samples()
    {
        Assert.True(MoadianBuyerValidator.IsValidNationalId("14014038299"));
        Assert.True(MoadianBuyerValidator.IsValidNationalId("1234567898"));
    }

    [Fact]
    public void R10_With_Zero_Control_Is_Valid()
    {
        // 3 * 29 = 87; 87 % 11 == 10 -> valid only when control digit is 0
        Assert.True(MoadianBuyerValidator.IsValidNationalId("30000000000"));
        Assert.True(MoadianBuyerValidator.IsValidNationalId("3000000000"));
        Assert.False(MoadianBuyerValidator.IsValidNationalId("30000000001"));
    }

    [Fact]
    public void Known_Invalid_Samples()
    {
        Assert.False(MoadianBuyerValidator.IsValidNationalId("14014038290")); // wrong control (11)
        Assert.False(MoadianBuyerValidator.IsValidNationalId("1234567890"));  // wrong control (10)
        Assert.False(MoadianBuyerValidator.IsValidNationalId("12345678901")); // wrong control (11)
        Assert.False(MoadianBuyerValidator.IsValidNationalId("123456789"));   // 9 digits
        Assert.False(MoadianBuyerValidator.IsValidNationalId("140140382991")); // 12 digits
        Assert.False(MoadianBuyerValidator.IsValidNationalId("1401403829A")); // letters
        Assert.False(MoadianBuyerValidator.IsValidNationalId(null!));
    }
}
