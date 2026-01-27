using System.ComponentModel.DataAnnotations;

namespace Midianita.Aplication.ViewModel
{
    public class RequestDesign
    {
        [Required(ErrorMessage = "O nome é obrigatório")]
        public string Name { get; set; }

        public string? CanvasData { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        [Required]
        public string CategoryId { get; set; }
    }
}
