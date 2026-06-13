using System.ComponentModel.DataAnnotations;

namespace FundingPlatform.Web.ViewModels.Admin;

/// <summary>Spec 035 / US1 — ViewModel for the /Admin/Categories list.</summary>
public class CategoryAdminViewModel
{
    public List<CategoryListItemViewModel> Categories { get; set; } = new();
}

public class CategoryListItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int FieldCount { get; set; }
}

/// <summary>Spec 035 / US1 — ViewModel for /Admin/CreateCategory.</summary>
public class CreateCategoryViewModel
{
    [Required(ErrorMessage = "El nombre de la categoría es obligatorio.")]
    [Display(Name = "Nombre")]
    [MaxLength(200, ErrorMessage = "El nombre debe tener máximo {1} caracteres.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Descripción")]
    [MaxLength(500, ErrorMessage = "La descripción debe tener máximo {1} caracteres.")]
    public string? Description { get; set; }

    public List<CategoryFieldDefinitionViewModel> Fields { get; set; } = new();
}

/// <summary>Spec 035 / US1 — ViewModel for /Admin/EditCategory.</summary>
public class EditCategoryViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre de la categoría es obligatorio.")]
    [Display(Name = "Nombre")]
    [MaxLength(200, ErrorMessage = "El nombre debe tener máximo {1} caracteres.")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Descripción")]
    [MaxLength(500, ErrorMessage = "La descripción debe tener máximo {1} caracteres.")]
    public string? Description { get; set; }

    [Display(Name = "Activa")]
    public bool IsActive { get; set; } = true;

    public List<CategoryFieldDefinitionViewModel> Fields { get; set; } = new();
}

/// <summary>Spec 035 / US1 — one repeating category-field row in the admin editor.</summary>
public class CategoryFieldDefinitionViewModel
{
    [Required(ErrorMessage = "El nombre del campo es obligatorio.")]
    [Display(Name = "Nombre")]
    [MaxLength(200, ErrorMessage = "El nombre debe tener máximo {1} caracteres.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "La etiqueta a mostrar es obligatoria.")]
    [Display(Name = "Etiqueta")]
    [MaxLength(300, ErrorMessage = "La etiqueta debe tener máximo {1} caracteres.")]
    public string DisplayLabel { get; set; } = string.Empty;

    [Required(ErrorMessage = "El tipo de dato es obligatorio.")]
    [Display(Name = "Tipo de dato")]
    public string DataType { get; set; } = "Text";

    [Display(Name = "Obligatorio")]
    public bool IsRequired { get; set; }

    [Display(Name = "Orden")]
    public int SortOrder { get; set; }
}
