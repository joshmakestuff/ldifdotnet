namespace LdifDotNet.Tests;

public class LdifValueTests
{
    [Fact]
    public void Mutating_source_array_does_not_change_value_or_hash()
    {
        byte[] source = [1, 2, 3];
        var value = LdifValue.FromBytes(source);
        int hashBefore = value.GetHashCode();

        source[0] = 99;

        Assert.Equal(new byte[] { 1, 2, 3 }, value.AsBytes());
        Assert.Equal(hashBefore, value.GetHashCode());
        Assert.Equal(LdifValue.FromBytes([1, 2, 3]), value);
    }

    [Fact]
    public void Mutating_returned_array_does_not_change_value_or_hash()
    {
        var value = LdifValue.FromBytes([1, 2, 3]);
        int hashBefore = value.GetHashCode();

        value.AsBytes()[0] = 99;

        Assert.Equal(new byte[] { 1, 2, 3 }, value.AsBytes());
        Assert.Equal(hashBefore, value.GetHashCode());
    }

    [Fact]
    public void Value_survives_as_hash_set_member_despite_source_mutation()
    {
        byte[] source = [10, 20, 30];
        var value = LdifValue.FromBytes(source);
        var set = new HashSet<LdifValue> { value };

        source[0] = 0;

        Assert.Contains(LdifValue.FromBytes([10, 20, 30]), set);
    }

    [Fact]
    public void FromUrl_rejects_relative_uris()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            LdifValue.FromUrl(new Uri("relative/photo.jpg", UriKind.Relative)));
        Assert.Contains("absolute", ex.Message);
    }

    [Fact]
    public void Text_and_binary_values_with_equal_octets_are_equal()
    {
        var text = LdifValue.FromString("abc");
        var binary = LdifValue.FromBytes("abc"u8.ToArray());

        Assert.Equal(text, binary);
        Assert.Equal(text.GetHashCode(), binary.GetHashCode());
    }
}
