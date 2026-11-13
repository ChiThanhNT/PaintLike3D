# PaintLike — Design System Master (`MASTER.md`)

> Generated following `ui-ux-pro-max`, `design-system`, and `ui-styling` skills.
> **Product Type:** Creative Drawing Canvas & Photo Editor Desktop Tool (`.NET 8 WinForms` + `OpenCV`)
> **Style Direction:** Bright Studio Light + Swiss Minimalist Precision (Sáng sủa, hạn chế tối đa màu đen trên thanh công cụ)

---

## 1. Three-Layer Token Architecture

### Layer 1: Primitive Tokens
| Token | Hex | RGB | Role |
|-------|-----|-----|------|
| `white` | `#FFFFFF` | `255, 255, 255` | Khung nhóm công cụ, nút bấm, mặt thẻ |
| `slate-50` | `#F8FAFC` | `248, 250, 252` | Nền thanh công cụ chính & bảng điều khiển |
| `slate-100` | `#F1F5F9` | `241, 245, 249` | Nền vùng làm việc bao quanh Canvas & Status Bar |
| `slate-200` | `#E2E8F0` | `226, 232, 240` | Viền nhẹ phân tách các nhóm công cụ |
| `slate-300` | `#CBD5E1` | `203, 213, 225` | Viền control tiêu chuẩn |
| `slate-600` | `#475569` | `71, 85, 105` | Chữ phụ & Icon mặc định (thay cho màu đen) |
| `navy-800` | `#1E3A5F` | `30, 58, 95` | Chữ chính (Xanh Navy đậm thanh lịch, không dùng đen `#000000`) |
| `indigo-50` | `#EEF2FF` | `238, 242, 255` | Nền tiêu đề bảng Layer & trạng thái Hover |
| `indigo-100` | `#E0E7FF` | `224, 231, 255` | Nền nút công cụ đang được chọn (Active Tool) |
| `indigo-600` | `#4F46E5` | `79, 70, 229` | Màu nhấn chủ đạo (Primary Accent) |
| `sky-600` | `#0284C7` | `2, 132, 199` | Màu nhấn phụ cho Xuất ảnh & Bộ lọc OpenCV |
| `rose-600` | `#E11D48` | `225, 29, 72` | Nút xóa / cảnh báo |

### Layer 2: Semantic Tokens
- **`BgWorkspace`**: `#F1F5F9` — Vùng bao quanh trang vẽ sáng dịu
- **`BgToolbar`**: `#F8FAFC` — Thanh công cụ trắng sứ
- **`BgRibbonGroup`**: `#FFFFFF` — Khung nhóm công cụ trắng tinh khiết
- **`BgPanel`**: `#F8FAFC` — Nền bảng `LayerPanel` và hộp thoại
- **`BgSurface`**: `#FFFFFF` — Nền nút công cụ và dòng Layer
- **`BgSurfaceHover`**: `#EEF2FF` — Hiệu ứng di chuột ánh xanh sáng
- **`TextPrimary`**: `#1E3A5F` — Chữ chính tông Xanh Navy đậm (loại bỏ màu đen tuyền)
- **`IconDefault`**: `#334155` — Icon vector tông Xanh Xám dịu mắt

---

## 2. Typography & Spacing Scale

- **Font Family:** `Segoe UI` (UI controls), `Consolas` (coordinates, zoom, filter status)
- **Spacing Rhythm:** `4px` / `8px` / `12px` / `16px` grid system
