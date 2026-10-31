using System;
using System.Linq;
using QFramework;
namespace Game
{
    /// <summary>开局同步应用，缺失真实人物时使入口事务失败。</summary>
    public readonly struct SurvivorCharacterApplyEvent {}
    /// <summary>不可变商品与所有权快照；价格仅由正式配置提供。</summary>
    public sealed class SurvivorCharacterProduct
    {
        /// <summary>稳定商品身份，购买只提交此编号。</summary>
        public string Id {get;}
        /// <summary>现有整身造型编号，不改变角色属性。</summary>
        public int AppearanceId {get;}
        /// <summary>由权威配置提供的金币价格。</summary>
        public long Price {get;}
        /// <summary>角色显示名称的本地化键。</summary>
        public string NameKey {get;}
        /// <summary>本次一致账本快照中的永久拥有状态。</summary>
        public bool Owned {get;}
        /// <summary>构造当前商品只读展示快照。</summary>
        public SurvivorCharacterProduct(string id,int appearanceId,long price,string nameKey,bool owned)
        {Id=id;AppearanceId=appearanceId;Price=price;NameKey=nameKey;Owned=owned;}
    }
    /// <summary>同一次账本读取的余额及商品所有权。</summary>
    public sealed class SurvivorCharacterShopSnapshot
    {
        /// <summary>与商品拥有状态同次读取的金币余额。</summary>
        public long Balance {get;}
        /// <summary>本次独立商品快照数组，不作为权威目录。</summary>
        public SurvivorCharacterProduct[] Products {get;}
        /// <summary>组合一次账本读取的余额与商品快照。</summary>
        public SurvivorCharacterShopSnapshot(long balance,SurvivorCharacterProduct[] products){Balance=balance;Products=products;}
    }
    public sealed partial class SurvivorSystem
    {
        /// <summary>坏档不重置；默认角色无需占用购买记录。</summary>
        public bool TryReadCharacterShop(out SurvivorCharacterShopSnapshot snapshot,out string error)
        {
            snapshot=null;
            if(!this.GetUtility<IRuntimeConfigProvider>().TryGetSurvivorCharacters(out var products,out error))return false;
            goldStore??=new SurvivorGoldFileStore(1);
            if(!goldStore.TryReadSnapshot(out var balance,out var owned,out error))return false;
            snapshot=new SurvivorCharacterShopSnapshot(balance,products.Select(p=>new SurvivorCharacterProduct(p.Id,p.AppearanceId,p.Price,p.NameKey,owned.Contains(p.Id))).ToArray());
            Model.GoldBalance=balance;return true;
        }
        /// <summary>UI只传稳定身份，配置决定价格；同文件事务成功后才发布。</summary>
        public bool TryPurchaseCharacter(string productId,out string error)
        {
            if(!TryReadCharacterShop(out var shop,out error))return false;
            var product=shop.Products.FirstOrDefault(p=>p.Id==productId);
            if(product==null){error="character.unknown";return false;}
            if(!goldStore.TryPurchase(product.Id,product.Price,out var balance,out error))return false;
            Model.GoldBalance=balance;Publish();return true;
        }
        /// <summary>默认0免费；收费外观必须在当前有效目录内且已拥有，开局再次执行。</summary>
        public bool TrySelectCharacter(int appearanceId,out string error)
        {
            error=null;
            if(Model.Active){error="character.run_active";return false;}
            if(appearanceId!=0) {
                if(!TryReadCharacterShop(out var shop,out error))return false;
                var product=shop.Products.FirstOrDefault(p=>p.AppearanceId==appearanceId);
                if(product==null){error="character.unknown";return false;}
                if(!product.Owned){error="character.not_owned";return false;}
            }
            Model.SelectedCharacter=appearanceId;return true;
        }
    }
}
