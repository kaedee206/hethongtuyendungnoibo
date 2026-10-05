using System.ComponentModel.DataAnnotations;
using Ats.Web.Models.DTOs;
using Xunit;

namespace Ats.Web.Tests
{
    public class ValidationTests
    {
        [Theory]
        [InlineData("0987654321", true)]
        [InlineData("0387654321", true)]
        [InlineData("0587654321", true)]
        [InlineData("0787654321", true)]
        [InlineData("0887654321", true)]
        [InlineData("+84987654321", true)]
        [InlineData("84987654321", true)]
        [InlineData("0123456789", false)] // Invalid prefix (1)
        [InlineData("098765432", false)] // Too short
        [InlineData("09876543210", false)] // Too long
        [InlineData("+8498765432", false)] // Too short for +84
        [InlineData("84123456789", false)] // Invalid prefix for 84 (1)
        [InlineData("abcdefghij", false)] // Letters
        public void UpdateProfileRequestDto_PhoneNumber_Validation(string phoneNumber, bool expectedIsValid)
        {
            var dto = new UpdateProfileRequestDto { FullName = "Test", PhoneNumber = phoneNumber };
            var context = new ValidationContext(dto) { MemberName = nameof(UpdateProfileRequestDto.PhoneNumber) };
            var results = new System.Collections.Generic.List<ValidationResult>();

            bool isValid = Validator.TryValidateProperty(dto.PhoneNumber, context, results);

            Assert.Equal(expectedIsValid, isValid);
        }
    }
}
