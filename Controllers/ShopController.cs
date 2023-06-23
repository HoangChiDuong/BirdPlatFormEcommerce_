﻿using BirdPlatForm.UserRespon;
using BirdPlatFormEcommerce.Entity;
using BirdPlatFormEcommerce.Product;
using BirdPlatFormEcommerce.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using System.Linq;
using MimeKit.Cryptography;
using BirdPlatFormEcommerce.Helper;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BirdPlatFormEcommerce.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ShopController : ControllerBase
    {
        private readonly SwpContext _context;
        private readonly IManageProductService _manageProductService;
        private readonly IWebHostEnvironment _enviroment;

        public ShopController(SwpContext swp, IManageProductService manageProductService, IWebHostEnvironment enviroment)
        {
            _context = swp;
            _manageProductService = manageProductService;
            _enviroment = enviroment;
        }
        [HttpPost("registerShop")]

        public async Task<IActionResult> RegisterShop(ShopModel shopmodel)
        {
            int userId = getuserIDfromtoken();
            var isShop = await _context.TbShops.FirstOrDefaultAsync(s => s.UserId == userId);
            if (isShop != null)
            {
                return Ok(new ErrorRespon
                {
                    Error = false,
                    Message = "UserId have to shop"
                });
            }
            var shop = new TbShop
            {
                ShopName = shopmodel.shopName,
                Address = shopmodel.Address,
                Phone = shopmodel.Phone,

                UserId = userId,

            };
            _context.TbShops.Add(shop);
            await _context.SaveChangesAsync();
            var user = await _context.TbUsers.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user != null)
            {
                user.RoleId = "SP";
                user.IsShop = true; 
                await _context.SaveChangesAsync();
            }
            return Ok(new ErrorRespon
            {

                Message = "Register shop Success"

            });
        }

        private int getuserIDfromtoken()
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();
            if (authHeader != null && authHeader.StartsWith("Bearer "))
            {
                var token = authHeader.Substring("Bearer ".Length).Trim();
                var tokenHandler = new JwtSecurityTokenHandler();
                var jwtToken = tokenHandler.ReadJwtToken(token);
                var accountIdClaim = jwtToken.Claims.FirstOrDefault(c => c.Type == "UserId");
                if (accountIdClaim != null && int.TryParse(accountIdClaim.Value, out int UserId))
                {
                    return UserId;
                }
            }
            throw new InvalidOperationException("Invalid token or missing accountId claim.");
        }
        [HttpGet]
        public async Task<IActionResult> getMyshop()
        {
            var myshop = User.Claims.FirstOrDefault(u => u.Type == "UserId");
            if(myshop == null)
            {
                return NotFound();
            }
            int userid = int.Parse(myshop.Value);
            var shop = _context.TbShops.FirstOrDefault(x => x.UserId == userid);
            if(shop == null)
            {
                return NotFound();
            }
            var isshop = new ViewShop
            {
                Rate = (int)shop.Rate,
                shopName = shop.ShopName,
                Address = shop.Address,
                phone = shop.Phone,
                
            };
            return Ok(isshop);
        }
        [HttpGet("getproductshop")]
        public async Task<List<HomeViewProductModel>> getProductShop()
        {
            var userIdclaim = User.Claims.FirstOrDefault(u => u.Type == "UserId");
            if (userIdclaim == null)
            {
                throw new Exception("User not found");
            }
            int userid = int.Parse(userIdclaim.Value);
            var shop = await _context.TbShops.FirstOrDefaultAsync(s => s.UserId == userid);
            if (shop == null)
            {
                throw new Exception("Shop not found");
            }
            int shopid = shop.ShopId;
            var query = from p in _context.TbProducts
                        join c in _context.TbProductCategories on p.CateId equals c.CateId
                        join img in _context.TbImages on p.ProductId equals img.ProductId
                        where p.ShopId == shopid && img.Caption == "Thumbnail"
                        select new { p, c, img };

            var data = await query.Select(x => new HomeViewProductModel()
            {
                ProductId = x.p.ProductId,
                ProductName = x.p.Name,
                CateName = x.c.CateName,
                Status = x.p.Status,
                Price = x.p.Price,
                DiscountPercent = x.p.DiscountPercent,
                SoldPrice = (int)Math.Round((decimal)(x.p.Price - x.p.Price / 100 * x.p.DiscountPercent)),
                QuantitySold = x.p.QuantitySold,
                Rate = x.p.Rate,
                Thumbnail = x.img != null ? x.img.ImagePath : "no-image.jpg",
            }).ToListAsync();

            return data;
        }


        [HttpPost]
        [Route("Add_Product")]
        public async Task<IActionResult> AddProduct([FromForm] CreateProductViewModel request)
        {
            try
            {
                var userIdClaim = User.Claims.FirstOrDefault(u => u.Type == "UserId");
                if (userIdClaim == null)
                {
                    throw new Exception("User not found");
                }
                int userid = int.Parse(userIdClaim.Value);
                var shop = await _context.TbShops.FirstOrDefaultAsync(s => s.UserId == userid);
                if (shop == null)
                {
                    throw new Exception("Shop not found");
                }
                int shopid = shop.ShopId;

                var product = new TbProduct()
                {
                    Name = request.ProductName,

                    Price = request.Price,
                    DiscountPercent = request.DiscountPercent,
                    SoldPrice = (int)Math.Round((decimal)(request.Price - request.Price / 100 * request.DiscountPercent)),
                    Decription = request.Decription,
                   
                    //          CreateDate = request.CreateDate,
                    Quantity = request.Quantity,
                   
                    ShopId = shopid,
                    CateId = request.CateId
                };

                if (!ModelState.IsValid)
                {
                    return BadRequest(ModelState);
                }
                _context.TbProducts.Add(product);
                await _context.SaveChangesAsync();


                //add image
                APIResponse response = new APIResponse();
                int passcount = 0;
                int errorcount = 0;
                try
                {

                    string Filepath = GetFileProductPath(product.ProductId);
                    if (!Directory.Exists(Filepath))
                    {
                        Directory.CreateDirectory(Filepath);
                    }
                    foreach (var file in request.ImageFile)
                    {

                        var image = new TbImage()
                        {

                            Caption = "Image",
                            CreateDate = DateTime.Now,
                          
                            ProductId = product.ProductId,

                            ImagePath = GetImageProductPath(product.ProductId, file.FileName),



                        };
                        string imagepath = Path.Combine(Filepath,file.FileName);
                        if (System.IO.File.Exists(imagepath))
                        {
                            System.IO.File.Delete(imagepath);
                        }
                        using (FileStream stream = System.IO.File.Create(imagepath))
                        {
                            await file.CopyToAsync(stream);
                            passcount++;
                        }
                        _context.Add(image);

                    }
                    await _context.SaveChangesAsync();

                }
                catch (Exception ex)
                {
                    errorcount++;
                    response.Errormessage = ex.Message;
                }

                response.ResponseCode = 200;
                response.Result = passcount + "File uploaded &" + errorcount + "File failed";
                return Ok(response);



              
            }
            catch
            {
                return BadRequest("Cannot Add check Again");
            }

        }

        [HttpPut("Update_Product")]
        public async Task<IActionResult> UpdateProduct(UpdateProductViewModel request)
        {
            try
            {
                var userIdClaim = User.Claims.FirstOrDefault(u => u.Type == "UserId");
                if (userIdClaim == null)
                {
                    return BadRequest("Can not find User");
                }
                int userId = int.Parse(userIdClaim.Value);
                var shop = await _context.TbShops.FirstOrDefaultAsync(x => x.UserId == userId);
                //       var pro = await _context.TbProducts.FirstOrDefaultAsync(x => x.ShopId == shop.ShopId);
                if (shop == null)
                {
                    throw new Exception("Shop not found");
                }
                //       int shopId = shop.ShopId;


                var product = await _context.TbProducts.FindAsync(request.ProductId);

                if (product == null) throw new Exception("Can not found.");
                product.Name = request.Name;
                product.Price = request.Price;
                product.DiscountPercent = request.DiscountPercent;
                product.SoldPrice = (int)Math.Round((decimal)(product.Price - request.Price / (100 * request.DiscountPercent)));
                product.Status = request.Status;
                product.Decription = request.Decription;
                product.Detail = request.Detail;
                await _context.SaveChangesAsync();
                return Ok("Update Success ");
            }
            catch
            {
                return BadRequest("CanNot update check again");
            }
        }


        [HttpDelete("Delete_Product")]
        public async Task<IActionResult> DeleteProduct(int productId)
        {
            
                var userIdClaim = User.Claims.FirstOrDefault(u => u.Type == "UserId");
                if (userIdClaim == null)
                {
                    return BadRequest("Can not find User");
                }
                int userId = int.Parse(userIdClaim.Value);
                var shop = await _context.TbShops.FirstOrDefaultAsync(x => x.UserId == userId);
                //       var pro = await _context.TbProducts.FirstOrDefaultAsync(x => x.ShopId == shop.ShopId);
                if (shop == null)
                {
                    throw new Exception("Shop not found");
                }

                if (!ModelState.IsValid)
                {
                    return BadRequest();
                }
                var product = await _context.TbProducts.FindAsync(productId);
                if (product == null) throw new Exception("Can not find product.");
            var oderDetail = await _context.TbOrderDetails.Where(o => o.ProductId == productId).ToListAsync();
            foreach(var oderdetail in oderDetail)
            {
                oderdetail.ProductId = 1;
            }
              var productImage = await _context.TbImages.Where(x => x.ProductId == productId).ToListAsync();

            
              _context.TbImages.RemoveRange(productImage);
             _context.TbProducts.Remove(product);



                await _context.SaveChangesAsync();
                return Ok("Delete Product Success");
           
        }


        private string GetFileProductPath(int productId)
        {
            return this._enviroment.WebRootPath + "\\user-content\\product\\" + productId.ToString();
        }


        private string GetImageProductPath(int productId, string fileName)
        {
            string hosturl = $"{this.Request.Scheme}://{this.Request.Host}{this.Request.PathBase}";
            return hosturl + "/user-content/product/" + productId + "/" + fileName ;

        }


        [HttpGet("AllProduct/shop")]
        public async Task<IActionResult> GetallProduct()
        {
            var userid = User.Claims.FirstOrDefault(x => x.Type == "UserId");
            if(userid == null)
            {
                return Unauthorized();
            }
            int user = int.Parse(userid.Value);
            var product = await _context.TbProducts.CountAsync(a => a.ShopId == user);
            if (product == null) return BadRequest("No Product");
            return Ok(product);
        }

    }
}
