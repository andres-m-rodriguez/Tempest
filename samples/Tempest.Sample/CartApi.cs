namespace Tempest.Sample;

public record CartItem(string Sku, string Name, decimal Price);

/// <summary>Stand-in for a real backend so the sample is self-contained.</summary>
public class CartApi
{
    public async Task<List<CartItem>> GetCartAsync()
    {
        await Task.Delay(300);
        return
        [
            new("SKU-1", "Storm Cloak", 49.99m),
            new("SKU-2", "Gale Boots", 89.00m),
        ];
    }

    public async Task<List<CartItem>> ApplyCouponAsync(string code)
    {
        var items = await GetCartAsync();
        return [.. items.Select(i => i with { Price = i.Price * 0.9m })];
    }
}
