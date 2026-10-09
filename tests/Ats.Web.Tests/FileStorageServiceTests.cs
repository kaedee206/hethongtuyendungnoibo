using Ats.Web.Data;
using Ats.Web.Models.Entities;
using Ats.Web.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Ats.Web.Tests;

public class FileStorageServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly Mock<IWebHostEnvironment> _mockEnv;
    private readonly Mock<ILogger<FileStorageService>> _mockLogger;
    private readonly FileStorageService _storageService;

    public FileStorageServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "AtsTestStorage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "wwwroot"));

        _mockEnv = new Mock<IWebHostEnvironment>();
        _mockEnv.Setup(e => e.WebRootPath).Returns(Path.Combine(_tempRoot, "wwwroot"));
        _mockEnv.Setup(e => e.ContentRootPath).Returns(_tempRoot);

        _mockLogger = new Mock<ILogger<FileStorageService>>();
        _storageService = new FileStorageService(_mockEnv.Object, _mockLogger.Object);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }
        catch { }
    }

    [Fact]
    public void EnsureStorageDirectories_CreatesSeparatedMediaAndCvsFolders()
    {
        // Act
        _storageService.EnsureStorageDirectories();

        // Assert
        var mediaPath = Path.Combine(_tempRoot, "wwwroot", "uploads", "media");
        var cvsPath = Path.Combine(_tempRoot, "wwwroot", "uploads", "cvs");

        Assert.True(Directory.Exists(mediaPath), "Thư mục uploads/media phải được tạo.");
        Assert.True(Directory.Exists(cvsPath), "Thư mục uploads/cvs phải được tạo.");
    }

    [Fact]
    public async Task SaveMediaAsync_ValidImage_SavesIntoMediaFolder()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();
        var content = "dummy image data"u8.ToArray();
        var stream = new MemoryStream(content);
        mockFile.Setup(f => f.FileName).Returns("candidate_avatar.png");
        mockFile.Setup(f => f.Length).Returns(content.Length);
        mockFile.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((s, ct) => s.WriteAsync(content, ct).AsTask());

        // Act
        var relativeUrl = await _storageService.SaveMediaAsync(mockFile.Object, "candidate");

        // Assert
        Assert.StartsWith("/uploads/media/", relativeUrl);
        Assert.EndsWith(".png", relativeUrl);
        Assert.True(_storageService.FileExists(relativeUrl));
    }

    [Fact]
    public async Task SaveMediaAsync_InvalidExtension_ThrowsArgumentException()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("virus.exe");
        mockFile.Setup(f => f.Length).Returns(100);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storageService.SaveMediaAsync(mockFile.Object));
    }

    [Fact]
    public async Task SaveCvAsync_ValidPdf_SavesIntoCvsFolder()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();
        var content = "%PDF-1.4 sample cv content"u8.ToArray();
        mockFile.Setup(f => f.FileName).Returns("NguyenVanA_CV.pdf");
        mockFile.Setup(f => f.Length).Returns(content.Length);
        mockFile.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((s, ct) => s.WriteAsync(content, ct).AsTask());

        // Act
        var relativeUrl = await _storageService.SaveCvAsync(mockFile.Object, "cv_candidate");

        // Assert
        Assert.StartsWith("/uploads/cvs/", relativeUrl);
        Assert.EndsWith(".pdf", relativeUrl);
        Assert.True(_storageService.FileExists(relativeUrl));
    }

    [Fact]
    public async Task SaveCvAsync_InvalidExtension_ThrowsArgumentException()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.FileName).Returns("script.bat");
        mockFile.Setup(f => f.Length).Returns(100);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _storageService.SaveCvAsync(mockFile.Object));
    }

    [Fact]
    public async Task DeleteFileAsync_ExistingFile_ReturnsTrueAndDeletes()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();
        var content = "data"u8.ToArray();
        mockFile.Setup(f => f.FileName).Returns("avatar.jpg");
        mockFile.Setup(f => f.Length).Returns(content.Length);
        mockFile.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<Stream, CancellationToken>((s, ct) => s.WriteAsync(content, ct).AsTask());

        var relativeUrl = await _storageService.SaveMediaAsync(mockFile.Object);

        // Act
        var deleted = await _storageService.DeleteFileAsync(relativeUrl);

        // Assert
        Assert.True(deleted);
        Assert.False(_storageService.FileExists(relativeUrl));
    }

    [Fact]
    public async Task GenerateExcelTemplateAsync_GeneratesThreeSheetsWithRichInstructions()
    {
        // Arrange
        using var db = TestDbContextFactory.CreateInMemoryDbContext();
        db.Departments.Add(new Department { Id = Guid.NewGuid(), Name = "Phòng Công Nghệ Thông Tin", IsActive = true });
        db.JobPositions.Add(new JobPosition { Id = Guid.NewGuid(), Title = "Senior Software Engineer (.NET & React)", IsActive = true });
        await db.SaveChangesAsync();

        var mockEmail = new Mock<Ats.Web.Services.Interfaces.IEmailService>();
        var userService = new UserService(db, mockEmail.Object);

        // Act
        var excelBytes = await userService.GenerateExcelTemplateAsync();

        // Lưu mẫu thực tế vào wwwroot/templates/Mau_Nhap_Nhan_Su.xlsx
        try
        {
            var projectTemplatesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "Ats.Web", "wwwroot", "templates"));
            if (Directory.Exists(projectTemplatesDir))
            {
                File.WriteAllBytes(Path.Combine(projectTemplatesDir, "Mau_Nhap_Nhan_Su.xlsx"), excelBytes);
            }
        }
        catch { }

        // Assert
        Assert.NotNull(excelBytes);
        Assert.True(excelBytes.Length > 0);

        using var ms = new MemoryStream(excelBytes);
        using var workbook = new XLWorkbook(ms);

        Assert.Equal(3, workbook.Worksheets.Count);
        Assert.NotNull(workbook.Worksheets.FirstOrDefault(w => w.Name == "Hướng dẫn & Quy định"));
        Assert.NotNull(workbook.Worksheets.FirstOrDefault(w => w.Name == "Dữ liệu"));
        Assert.NotNull(workbook.Worksheets.FirstOrDefault(w => w.Name == "Danh mục tham chiếu"));

        var dataSheet = workbook.Worksheet("Dữ liệu");
        Assert.Equal("Họ và tên (*)", dataSheet.Cell(1, 1).GetString().Trim());
        Assert.Equal("Email (*)", dataSheet.Cell(1, 2).GetString().Trim());
        Assert.Equal("Số điện thoại", dataSheet.Cell(1, 3).GetString().Trim());
        Assert.Equal("Phòng ban", dataSheet.Cell(1, 4).GetString().Trim());
        Assert.Equal("Chức vụ", dataSheet.Cell(1, 5).GetString().Trim());
        Assert.Equal("Vai trò (*)", dataSheet.Cell(1, 6).GetString().Trim());

        // Kiểm tra dòng dữ liệu mẫu có đuôi @noveratech.digital
        var sampleEmail = dataSheet.Cell(2, 2).GetString().Trim();
        Assert.EndsWith("@noveratech.digital", sampleEmail);
    }
}
