from PIL import Image, ImageDraw, ImageFilter

SIZE = 256
SS = 4  # 超采样倍数

logo = Image.open("AppLogo@768w.png").convert("RGBA").resize((SIZE*SS, SIZE*SS), Image.LANCZOS)
fan = Image.open("fan_blue.png").convert("RGBA")

canvas = Image.new("RGBA", (SIZE*SS, SIZE*SS), (0,0,0,0))
canvas.alpha_composite(logo)

# ---- 右下角徽标 ----
cx, cy = int(SIZE*SS*0.755), int(SIZE*SS*0.755)
r = int(SIZE*SS*0.215)

# 阴影
shadow = Image.new("RGBA", canvas.size, (0,0,0,0))
sd = ImageDraw.Draw(shadow)
sd.ellipse([cx-r, cy-r+int(SIZE*SS*0.018), cx+r, cy+r+int(SIZE*SS*0.018)], fill=(0,0,0,60))
shadow = shadow.filter(ImageFilter.GaussianBlur(int(SIZE*SS*0.02)))
canvas.alpha_composite(shadow)

# 白色底盘 + 细边框
badge = Image.new("RGBA", canvas.size, (0,0,0,0))
bd = ImageDraw.Draw(badge)
bd.ellipse([cx-r, cy-r, cx+r, cy+r], fill=(255,255,255,255), outline=(226,226,226,255), width=int(SIZE*SS*0.009))
canvas.alpha_composite(badge)

# 风扇（约占徽标内圈 82%）
fan_size = int(r*2*0.90)
fan_r = fan.resize((fan_size, fan_size), Image.LANCZOS)
canvas.alpha_composite(fan_r, (cx - fan_size//2, cy - fan_size//2))

out = canvas.resize((SIZE, SIZE), Image.LANCZOS)
out.save("icon.png")
print("saved icon.png", out.size)

# 小尺寸预览，检查可读性
for s in (64, 32, 16):
    out.resize((s, s), Image.LANCZOS).save(f"preview_{s}.png")
print("previews saved")
