using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.OpenApi.Models;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);


void WriteLog(string message)
{
    var logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
    Directory.CreateDirectory(logDirectory); // สร้างโฟลเดอร์ Logs ถ้ายังไม่มี

    var logFile = Path.Combine(logDirectory, $"{DateTime.Now:yyyy-MM-dd}.log");

    var fullMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
    File.AppendAllText(logFile, fullMessage); // เขียนต่อท้าย
}

if (WindowsServiceHelpers.IsWindowsService())
{
    var pathToExe = Process.GetCurrentProcess().MainModule?.FileName;
var pathToContentRoot = Path.GetDirectoryName(pathToExe) ?? Directory.GetCurrentDirectory();

builder.Host.UseContentRoot(pathToContentRoot);
    builder.Host.UseWindowsService(); // 👈 สำคัญ!
}


builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
builder.Services.AddDbContext<AppDbContext>(option => option.UseSqlite("Data Source=secretfiles.db"));
builder.WebHost.UseUrls("http://localhost:5278"); // หรือ http://localhost:5000

var app = builder.Build();

app.UseCors();
app.UseStaticFiles();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

string GetSecretfileDetail(SecretfileSend file)
{
    return $"เลขที่ส่ง: {file.Send_number}, ชั้นความลับ: {file.Secret_layer}, ลงวันที่: {file.Date}, จาก: {file.From}, ถึง: {file.To}, เรื่อง: {file.Subject}, ลงชื่อ: {file.Sign}, ไฟล์: {file.File}";
}

string GetSecretfileDetail1(SecretfileReceive file)
{
    return $"เลขที่ส่ง: {file.Receive_number}, ชั้นความลับ: {file.Secret_layer}, ลงวันที่: {file.Date}, จาก: {file.From}, ถึง: {file.To}, เรื่อง: {file.Subject}, ลงชื่อ: {file.Sign}, ไฟล์: {file.File}";
}

app.MapPost("/api/logins", async (Login loginRequest, AppDbContext db) =>
{
    var user = await db.Logins
        .FirstOrDefaultAsync(l => l.Username == loginRequest.Username && l.Password == loginRequest.Password);

    if (user is null)
        return Results.Unauthorized();

    // 📝 เก็บ log การเข้าสู่ระบบ
    WriteLog($"[LOGIN] {user.Username} เข้าสู่ระบบ (role: {user.Role})");

    // 🔐 ส่ง role กลับไปด้วย
    return Results.Ok(new
    {
        user.Id,
        user.Username,
        user.Role
    });
});

app.MapGet("/api/users", async (AppDbContext db) => await db.Logins.ToListAsync());
app.MapPost("/api/users", async (HttpRequest request, AppDbContext db) =>
{
    var form = await request.ReadFormAsync();

    var username = form["username"].ToString();  // ✅ แปลงเป็น string
    var password = form["password"].ToString();
    var role = form["role"].ToString();

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        return Results.BadRequest("Username และ Password ต้องไม่ว่าง");

    var existingUser = await db.Logins.FirstOrDefaultAsync(u => u.Username == username);
    if (existingUser != null)
        return Results.Conflict("มีผู้ใช้นี้อยู่แล้ว");

    var newUser = new Login
    {
        Username = username,
        Password = password,
        Role = role
    };

    db.Logins.Add(newUser);
    await db.SaveChangesAsync();

    WriteLog($"[ADD USER] เพิ่มผู้ใช้ใหม่: {username} (role: {role})");

    return Results.Created($"/api/users/{newUser.Id}", newUser);
});
app.MapPut("/api/users/{id}", async (int id, HttpRequest request, AppDbContext db) =>
{
    var form = await request.ReadFormAsync();

    var username = form["username"].ToString();
    var password = form["password"].ToString();
    var role = form["role"].ToString();

    var existingUser = await db.Logins.FindAsync(id);
    if (existingUser == null)
        return Results.NotFound("ไม่พบผู้ใช้ที่ต้องการแก้ไข");

    // ปรับค่าจากฟอร์ม
    existingUser.Username = username;
    if (!string.IsNullOrWhiteSpace(password))
    {
        existingUser.Password = password; // หรือคุณอาจแยก logic ว่า ถ้าแก้ไข password เท่านั้นค่อยอัปเดต
    }
    existingUser.Role = role;

    await db.SaveChangesAsync();

    WriteLog($"[EDIT USER] แก้ไขผู้ใช้: {existingUser.Username} (role: {existingUser.Role})");

    return Results.Ok(existingUser);
});
app.MapDelete("/api/users/{id:int}", async (int id, AppDbContext db) =>
{
    var user = await db.Logins.FindAsync(id);
    if (user == null)
        return Results.NotFound("ไม่พบผู้ใช้นี้");

    db.Logins.Remove(user);
    await db.SaveChangesAsync();

    WriteLog($"[DELETE USER] ลบผู้ใช้: {user.Username}");

    return Results.NoContent();
});

app.MapGet("/api/secretfilessend", async (AppDbContext db) => await db.SecretfilesSend.ToListAsync());
app.MapPost("/api/secretfilessend", async (HttpRequest request, AppDbContext db) =>
{
    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();


    var file = form.Files["file"];
    string fileName = null!;

    if (file != null && file.Length > 0)
    {
        var uploadsFolder = Path.Combine("wwwroot", "uploads", "send");
        Directory.CreateDirectory(uploadsFolder); // เผื่อยังไม่มี

        fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
        var filePath = Path.Combine(uploadsFolder, fileName);

        using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);
    }else{
        fileName = "";
    }

     // 🛑 เช็คว่า send_number ซ้ำไหม
    var sendNumber = int.Parse(form["send_number"]!);
    var isDuplicate = await db.SecretfilesSend.AnyAsync(f => f.Send_number == sendNumber);
    if (isDuplicate)
    {
        return Results.BadRequest(new
        {
            message = $"เลขที่ส่งหนังสือ '{sendNumber}' มีอยู่ในระบบแล้ว"
        });
    }


    var secretfile = new SecretfileSend
    {
        Send_number = int.Parse(form["send_number"]!),
        Secret_layer = int.Parse(form["secret_layer"]!),
        Date = form["date"]!,
        From = form["from"]!,
        To = form["to"]!,
        Subject = form["subject"]!,
        Sign = form["sign"]!,
        File = fileName // ✅ เก็บแค่ชื่อไฟล์ เช่น: "a1b2c3.pdf"
    };

    db.SecretfilesSend.Add(secretfile);
    await db.SaveChangesAsync();

    WriteLog($"[ADD] โดย {username} -> {GetSecretfileDetail(secretfile)}");

    return Results.Created($"/api/secretfiles/{secretfile.Id}", secretfile);
});
app.MapPut("/api/secretfilessend/{id:int}", async (int id, HttpRequest request, AppDbContext db) =>
{

    var secretfile = await db.SecretfilesSend.FindAsync(id);
    if (secretfile == null) return Results.NotFound();
    var old = secretfile;
    var form = await request.ReadFormAsync();

    var username = form["username"].ToString();

if (!int.TryParse(form["send_number"], out var sendNumber))
    return Results.BadRequest("send_number ต้องเป็นตัวเลข");

if (!int.TryParse(form["secret_layer"], out var secretLayer))
    return Results.BadRequest("secret_layer ต้องเป็นตัวเลข");


    var file = form.Files["file"];
    string? fileName = secretfile.File; // เก็บชื่อไฟล์เดิมไว้
    if (file != null && file.Length > 0)
    {
        var uploadsFolder = Path.Combine("wwwroot", "uploads", "send");
        Directory.CreateDirectory(uploadsFolder);

        fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
        var filePath = Path.Combine(uploadsFolder, fileName);

        using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);
    }

    var newData = new SecretfileSend
{
    Send_number = int.Parse(form["send_number"]),
    Secret_layer = int.Parse(form["secret_layer"]),
    Date = form["date"],
    From = form["from"],
    To = form["to"],
    Subject = form["subject"],
    Sign = form["sign"],
    File = fileName // ไฟล์จะเปลี่ยนด้านล่าง
};

    

    string GetChangeLog(SecretfileSend old, SecretfileSend updated)
{
    var changes = new List<string>();

    if (old.Send_number != updated.Send_number)
        changes.Add($"เลขที่ส่ง: {old.Send_number} ➜ {updated.Send_number}");

    if (old.Secret_layer != updated.Secret_layer)
        changes.Add($"ชั้นความลับ: {old.Secret_layer} ➜ {updated.Secret_layer}");

    if (old.Date != updated.Date)
        changes.Add($"ลงวันที่: {old.Date} ➜ {updated.Date}");

    if (old.From != updated.From)
        changes.Add($"จาก: {old.From} ➜ {updated.From}");

    if (old.To != updated.To)
        changes.Add($"ถึง: {old.To} ➜ {updated.To}");

    if (old.Subject != updated.Subject)
        changes.Add($"เรื่อง: {old.Subject} ➜ {updated.Subject}");

    if (old.Sign != updated.Sign)
        changes.Add($"ลงชื่อ: {old.Sign} ➜ {updated.Sign}");

    if (old.File != updated.File)
        changes.Add($"ไฟล์: {old.File ?? "ไม่มี"} ➜ {updated.File}");

    return changes.Count == 0 ? "ไม่มีการเปลี่ยนแปลง" : string.Join(" | ", changes);
}



string changeLog = GetChangeLog(old, newData);
WriteLog($"[EDIT] โดย {username} แก้ไขไฟล์เลขที่ส่ง: {old.Send_number} | {changeLog}");

    secretfile.Send_number = newData.Send_number;
secretfile.Secret_layer = newData.Secret_layer;
secretfile.Date = newData.Date;
secretfile.From = newData.From;
secretfile.To = newData.To;
secretfile.Subject = newData.Subject;
secretfile.Sign = newData.Sign;
secretfile.File = newData.File;

await db.SaveChangesAsync();
    

    return Results.NoContent();
});
app.MapDelete("/api/secretfilessend/{id:int}", async (int id, HttpRequest request,  AppDbContext db) =>
{
    var file = await db.SecretfilesSend.FindAsync(id);
    if (file is null) return Results.NotFound();
    var username = request.Headers["username"].ToString(); // 👈 ดึงจาก header (ให้ Frontend ส่งมาด้วย)
    db.SecretfilesSend.Remove(file);
    if (!string.IsNullOrEmpty(file.File))
{
    var fullFilePath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads", "send" , file.File);
    if (System.IO.File.Exists(fullFilePath))
        System.IO.File.Delete(fullFilePath);
}
    WriteLog($"[DELETE] โดย {username} -> {GetSecretfileDetail(file)}");
    await db.SaveChangesAsync();
    return Results.NoContent();

    

});

app.MapGet("/api/receivedocs", async (AppDbContext db) => await db.SecretfileReceive.ToListAsync());
app.MapPost("/api/receivedocs", async (HttpRequest request, AppDbContext db) =>
{
    var form = await request.ReadFormAsync();
    var username = form["username"].ToString();


    var file = form.Files["file"];
    string fileName = null!;

    if (file != null && file.Length > 0)
    {
        var uploadsFolder = Path.Combine("wwwroot", "uploads", "receive");
        Directory.CreateDirectory(uploadsFolder); // เผื่อยังไม่มี

        fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
        var filePath = Path.Combine(uploadsFolder, fileName);

        using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);
    }else{
        fileName = "";
    }

    var receive_number = int.Parse(form["receive_number"]!);
    var isDuplicate = await db.SecretfileReceive.AnyAsync(f => f.Receive_number == receive_number);
    if (isDuplicate)
    {
        return Results.BadRequest(new
        {
            message = $"เลขที่รับหนังสือ '{receive_number}' มีอยู่ในระบบแล้ว"
        });
    }

    var secretfile = new SecretfileReceive
    {
        Receive_number = int.Parse(form["receive_number"]!),
        File_number = form["file_number"]!,
        Secret_layer = int.Parse(form["secret_layer"]!),
        Date = form["date"]!,
        From = form["from"]!,
        To = form["to"]!,
        Subject = form["subject"]!,
        Sign = form["sign"]!,
        Date1 = form["date1"]!,
        Note = form["note"]!,
        File = fileName // ✅ เก็บแค่ชื่อไฟล์ เช่น: "a1b2c3.pdf"
    };

    db.SecretfileReceive.Add(secretfile);
    await db.SaveChangesAsync();

    WriteLog($"[ADD] โดย {username} -> {GetSecretfileDetail1(secretfile)}");

    return Results.Created($"/api/secretfiles/{secretfile.Id}", secretfile);
});
app.MapPut("/api/receivedocs/{id:int}", async (int id, HttpRequest request, AppDbContext db) =>
{

    var secretfile = await db.SecretfileReceive.FindAsync(id);
    if (secretfile == null) return Results.NotFound();
    var old = secretfile;
    var form = await request.ReadFormAsync();

    var username = form["username"].ToString();

if (!int.TryParse(form["receive_number"], out var sendNumber))
    return Results.BadRequest("receive_number ต้องเป็นตัวเลข");

if (!int.TryParse(form["secret_layer"], out var secretLayer))
    return Results.BadRequest("secret_layer ต้องเป็นตัวเลข");

var file = form.Files["file"];
    string? fileName = secretfile.File; // เก็บชื่อไฟล์เดิมไว้
    if (file != null && file.Length > 0)
    {
        var uploadsFolder = Path.Combine("wwwroot", "uploads", "receive");
        Directory.CreateDirectory(uploadsFolder);

        fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
        var filePath = Path.Combine(uploadsFolder, fileName);

        using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);
    }

    var newData = new SecretfileReceive
{
    Receive_number = int.Parse(form["receive_number"]!),
        File_number = form["file_number"]!,
        Secret_layer = int.Parse(form["secret_layer"]!),
        Date = form["date"]!,
        From = form["from"]!,
        To = form["to"]!,
        Subject = form["subject"]!,
        Sign = form["sign"]!,
        Date1 = form["date1"]!,
        Note = form["note"]!,
        File = fileName // ✅ เก็บแค่ชื่อไฟล์ เช่น: "a1b2c3.pdf"
};

    

    string GetChangeLog(SecretfileReceive old, SecretfileReceive updated)
{
    var changes = new List<string>();

    if (old.Receive_number != updated.Receive_number)
        changes.Add($"เลขที่รับหนังสือ: {old.Receive_number} ➜ {updated.Receive_number}");

        if (old.File_number != updated.File_number)
        changes.Add($"เลขที่หนังสือ: {old.File_number} ➜ {updated.File_number}");

    if (old.Secret_layer != updated.Secret_layer)
        changes.Add($"ชั้นความลับ: {old.Secret_layer} ➜ {updated.Secret_layer}");

    if (old.Date != updated.Date)
        changes.Add($"วัน/เดือน/ปี: {old.Date} ➜ {updated.Date}");

    if (old.From != updated.From)
        changes.Add($"จาก: {old.From} ➜ {updated.From}");

    if (old.To != updated.To)
        changes.Add($"ถึง: {old.To} ➜ {updated.To}");

    if (old.Subject != updated.Subject)
        changes.Add($"เรื่อง: {old.Subject} ➜ {updated.Subject}");

    if (old.Sign != updated.Sign)
        changes.Add($"ลงชื่อ: {old.Sign} ➜ {updated.Sign}");

    if (old.Date1 != updated.Date1)
        changes.Add($"ลงวันที่: {old.Date1} ➜ {updated.Date1}");

    if (old.Note != updated.Note)
        changes.Add($"ถึง: {old.Note} ➜ {updated.Note}");

    if (old.File != updated.File)
        changes.Add($"ไฟล์: {old.File ?? "ไม่มี"} ➜ {updated.File}");

    return changes.Count == 0 ? "ไม่มีการเปลี่ยนแปลง" : string.Join(" | ", changes);
}



string changeLog = GetChangeLog(old, newData);
WriteLog($"[EDIT] โดย {username} แก้ไขไฟล์เลขที่ส่ง: {old.Receive_number} | {changeLog}");

    secretfile.Receive_number = newData.Receive_number;
    secretfile.File_number = newData.File_number;
secretfile.Secret_layer = newData.Secret_layer;
secretfile.Date = newData.Date;
secretfile.From = newData.From;
secretfile.To = newData.To;
secretfile.Subject = newData.Subject;
secretfile.Sign = newData.Sign;
secretfile.Date1 = newData.Date1;
secretfile.Note = newData.Note;
secretfile.File = newData.File;

await db.SaveChangesAsync();
    

    return Results.NoContent();
});
app.MapDelete("/api/receivedocs/{id:int}", async (int id, HttpRequest request,  AppDbContext db) =>
{
    var file = await db.SecretfileReceive.FindAsync(id);
    if (file is null) return Results.NotFound();
    var username = request.Headers["username"].ToString(); // 👈 ดึงจาก header (ให้ Frontend ส่งมาด้วย)
    db.SecretfileReceive.Remove(file);
    if (!string.IsNullOrEmpty(file.File))
{
    var fullFilePath = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "uploads", "receive", file.File);
    if (System.IO.File.Exists(fullFilePath))
        System.IO.File.Delete(fullFilePath);
}
    WriteLog($"[DELETE] โดย {username} -> {GetSecretfileDetail1(file)}");
    await db.SaveChangesAsync();
    return Results.NoContent();

    

});
app.Run();

public class Login
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
}
public class SecretfileSend
{
    public int Id { get; set; }
    public int Send_number { get; set; }
    public int Secret_layer { get; set; }
    public string Date { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Sign { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;

}
public class SecretfileReceive
{
    public int Id { get; set; }
    public int Receive_number { get; set; }
    public string File_number { get; set; } = string.Empty;
    public int Secret_layer { get; set; }
    public string Date { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Sign { get; set; } = string.Empty;
    public string Date1 { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;

}
public class AppDbContext : DbContext
{
    public DbSet<Login> Logins => Set<Login>();
    public DbSet<SecretfileSend> SecretfilesSend => Set<SecretfileSend>();
    public DbSet<SecretfileReceive> SecretfileReceive => Set<SecretfileReceive>();
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}

