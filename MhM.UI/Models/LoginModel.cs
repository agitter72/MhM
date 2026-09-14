using System.ComponentModel.DataAnnotations;

namespace MhM.UI.Models
{
    public class LoginModel
    {
        [Required, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
