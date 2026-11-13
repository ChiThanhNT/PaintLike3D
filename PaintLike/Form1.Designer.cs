namespace PaintLike
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            pictureBox1 = new PictureBox();
            panelTools = new Panel();
            btnExport = new Button();
            btnSave = new Button();
            BttRoundedRectangle = new Button();
            BttBezier = new Button();
            BttEllipse = new Button();
            BttArrows = new Button();
            BttDiamond = new Button();
            BttHexagon = new Button();
            btnSmartSelect = new Button();
            btnRedo = new Button();
            btnUndo = new Button();
            btnTrapezoid = new Button();
            btnStar = new Button();
            nudEraserSize = new NumericUpDown();
            btnTriangle = new Button();
            btnCircle = new Button();
            btnRect = new Button();
            btnLine = new Button();
            btnEraser = new Button();
            btnColor = new Button();
            btnImportImage = new Button();
            btnLayers = new Button();
            trackBarOpacity = new TrackBar();
            lblOpacity = new Label();
            colorDialog1 = new ColorDialog();
            ((System.ComponentModel.ISupportInitialize)pictureBox1).BeginInit();
            panelTools.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)nudEraserSize).BeginInit();
            ((System.ComponentModel.ISupportInitialize)trackBarOpacity).BeginInit();
            SuspendLayout();
            // 
            // pictureBox1
            // 
            pictureBox1.BackColor = Color.FromArgb(241, 245, 249);
            pictureBox1.Dock = DockStyle.Fill;
            pictureBox1.Location = new Point(0, 106);
            pictureBox1.Name = "pictureBox1";
            pictureBox1.Size = new Size(1220, 634);
            pictureBox1.TabIndex = 0;
            pictureBox1.TabStop = false;
            pictureBox1.Paint += pictureBox1_Paint;
            pictureBox1.MouseDown += pictureBox1_MouseDown;
            pictureBox1.MouseMove += pictureBox1_MouseMove;
            pictureBox1.MouseUp += pictureBox1_MouseUp;
            // 
            // panelTools
            // 
            panelTools.BackColor = Color.FromArgb(248, 250, 252);
            panelTools.Controls.Add(btnExport);
            panelTools.Controls.Add(btnSave);
            panelTools.Controls.Add(BttRoundedRectangle);
            panelTools.Controls.Add(BttBezier);
            panelTools.Controls.Add(BttEllipse);
            panelTools.Controls.Add(BttArrows);
            panelTools.Controls.Add(BttDiamond);
            panelTools.Controls.Add(BttHexagon);
            panelTools.Controls.Add(btnSmartSelect);
            panelTools.Controls.Add(btnRedo);
            panelTools.Controls.Add(btnUndo);
            panelTools.Controls.Add(btnTrapezoid);
            panelTools.Controls.Add(btnStar);
            panelTools.Controls.Add(nudEraserSize);
            panelTools.Controls.Add(btnTriangle);
            panelTools.Controls.Add(btnCircle);
            panelTools.Controls.Add(btnRect);
            panelTools.Controls.Add(btnLine);
            panelTools.Controls.Add(btnEraser);
            panelTools.Controls.Add(btnColor);
            panelTools.Controls.Add(btnImportImage);
            panelTools.Controls.Add(btnLayers);
            panelTools.Controls.Add(trackBarOpacity);
            panelTools.Controls.Add(lblOpacity);
            panelTools.Dock = DockStyle.Top;
            panelTools.Location = new Point(0, 0);
            panelTools.Name = "panelTools";
            panelTools.Size = new Size(1220, 106);
            panelTools.TabIndex = 1;
            // 
            // Group 1: FILE & HISTORY (X: 10..208)
            // 
            btnImportImage.Location = new Point(18, 12);
            btnImportImage.Name = "btnImportImage";
            btnImportImage.Size = new Size(56, 32);
            btnImportImage.TabIndex = 108;
            btnImportImage.Text = "Mở";
            btnImportImage.Click += btnImportImage_Click;
            // 
            btnSave.Location = new Point(78, 12);
            btnSave.Name = "btnSave";
            btnSave.Size = new Size(58, 32);
            btnSave.TabIndex = 121;
            btnSave.Text = "Lưu";
            btnSave.UseVisualStyleBackColor = false;
            btnSave.Click += btnSave_Click;
            // 
            btnExport.Location = new Point(140, 12);
            btnExport.Name = "btnExport";
            btnExport.Size = new Size(60, 32);
            btnExport.TabIndex = 122;
            btnExport.Text = "Xuất";
            btnExport.UseVisualStyleBackColor = false;
            btnExport.Click += btnExport_Click;
            // 
            btnUndo.Location = new Point(18, 48);
            btnUndo.Name = "btnUndo";
            btnUndo.Size = new Size(88, 30);
            btnUndo.TabIndex = 112;
            btnUndo.Text = "Hoàn tác";
            btnUndo.UseVisualStyleBackColor = true;
            btnUndo.Click += btnUndo_Click;
            // 
            btnRedo.Location = new Point(112, 48);
            btnRedo.Name = "btnRedo";
            btnRedo.Size = new Size(88, 30);
            btnRedo.TabIndex = 113;
            btnRedo.Text = "Làm lại";
            btnRedo.UseVisualStyleBackColor = true;
            btnRedo.Click += btnRedo_Click;
            // 
            // Group 2: TOOLS (X: 216..408)
            // 
            btnLine.Location = new Point(224, 12);
            btnLine.Name = "btnLine";
            btnLine.Size = new Size(85, 32);
            btnLine.TabIndex = 104;
            btnLine.Text = "Đường";
            btnLine.Click += btnLine_Click;
            // 
            BttBezier.Location = new Point(315, 12);
            BttBezier.Name = "BttBezier";
            BttBezier.Size = new Size(85, 32);
            BttBezier.TabIndex = 119;
            BttBezier.Text = "Cong";
            BttBezier.UseVisualStyleBackColor = false;
            BttBezier.Click += BttBezier_Click;
            // 
            btnEraser.Location = new Point(224, 48);
            btnEraser.Name = "btnEraser";
            btnEraser.Size = new Size(85, 30);
            btnEraser.TabIndex = 105;
            btnEraser.Text = "Tẩy";
            btnEraser.Click += btnEraser_Click;
            // 
            btnSmartSelect.Location = new Point(315, 48);
            btnSmartSelect.Name = "btnSmartSelect";
            btnSmartSelect.Size = new Size(85, 30);
            btnSmartSelect.TabIndex = 114;
            btnSmartSelect.Text = "Tách nền";
            btnSmartSelect.UseVisualStyleBackColor = true;
            btnSmartSelect.Click += btnSmartSelect_Click;
            // 
            // Group 3: SHAPES (X: 416..672)
            // 
            btnRect.Location = new Point(424, 12);
            btnRect.Name = "btnRect";
            btnRect.Size = new Size(44, 31);
            btnRect.TabIndex = 103;
            btnRect.Text = "Rectangle";
            btnRect.Click += btnRect_Click;
            // 
            BttRoundedRectangle.Location = new Point(473, 12);
            BttRoundedRectangle.Name = "BttRoundedRectangle";
            BttRoundedRectangle.Size = new Size(44, 31);
            BttRoundedRectangle.TabIndex = 120;
            BttRoundedRectangle.Text = "Rounded Rectangle";
            BttRoundedRectangle.UseVisualStyleBackColor = false;
            BttRoundedRectangle.Click += BttRoundedRectangle_Click;
            // 
            btnCircle.Location = new Point(522, 12);
            btnCircle.Name = "btnCircle";
            btnCircle.Size = new Size(44, 31);
            btnCircle.TabIndex = 102;
            btnCircle.Text = "Circle";
            btnCircle.Click += btnCircle_Click;
            // 
            BttEllipse.Location = new Point(571, 12);
            BttEllipse.Name = "BttEllipse";
            BttEllipse.Size = new Size(44, 31);
            BttEllipse.TabIndex = 118;
            BttEllipse.Text = "Ellipse";
            BttEllipse.UseVisualStyleBackColor = false;
            BttEllipse.Click += BttEllipse_Click;
            // 
            btnTriangle.Location = new Point(620, 12);
            btnTriangle.Name = "btnTriangle";
            btnTriangle.Size = new Size(44, 31);
            btnTriangle.TabIndex = 101;
            btnTriangle.Text = "Triangle";
            btnTriangle.Click += btnTriangle_Click;
            // 
            BttDiamond.Location = new Point(424, 47);
            BttDiamond.Name = "BttDiamond";
            BttDiamond.Size = new Size(44, 31);
            BttDiamond.TabIndex = 116;
            BttDiamond.Text = "Diamond";
            BttDiamond.UseVisualStyleBackColor = false;
            BttDiamond.Click += BttDiamond_Click;
            // 
            btnTrapezoid.Location = new Point(473, 47);
            btnTrapezoid.Name = "btnTrapezoid";
            btnTrapezoid.Size = new Size(44, 31);
            btnTrapezoid.TabIndex = 107;
            btnTrapezoid.Text = "Trapezoid";
            btnTrapezoid.Click += btnTrapezoid_Click;
            // 
            BttHexagon.Location = new Point(522, 47);
            BttHexagon.Name = "BttHexagon";
            BttHexagon.Size = new Size(44, 31);
            BttHexagon.TabIndex = 115;
            BttHexagon.Text = "Hexagon";
            BttHexagon.UseVisualStyleBackColor = false;
            BttHexagon.Click += BttHexagon_Click;
            // 
            btnStar.Location = new Point(571, 47);
            btnStar.Name = "btnStar";
            btnStar.Size = new Size(44, 31);
            btnStar.TabIndex = 0;
            btnStar.Text = "Star";
            btnStar.Click += btnStar_Click;
            // 
            BttArrows.Location = new Point(620, 47);
            BttArrows.Name = "BttArrows";
            BttArrows.Size = new Size(44, 31);
            BttArrows.TabIndex = 117;
            BttArrows.Text = "Arrows";
            BttArrows.UseVisualStyleBackColor = false;
            BttArrows.Click += BttArrows_Click;
            // 
            // Group 4: STROKE & COLOR (X: 680..1062)
            // 
            btnColor.Location = new Point(688, 12);
            btnColor.Name = "btnColor";
            btnColor.Size = new Size(84, 32);
            btnColor.TabIndex = 106;
            btnColor.Text = "Màu vẽ";
            btnColor.Click += btnColor_Click;
            // 
            nudEraserSize.BackColor = Color.White;
            nudEraserSize.ForeColor = Color.FromArgb(30, 58, 95);
            nudEraserSize.BorderStyle = BorderStyle.FixedSingle;
            nudEraserSize.Location = new Point(892, 15);
            nudEraserSize.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
            nudEraserSize.Minimum = new decimal(new int[] { 5, 0, 0, 0 });
            nudEraserSize.Name = "nudEraserSize";
            nudEraserSize.Size = new Size(54, 25);
            nudEraserSize.TabIndex = 100;
            nudEraserSize.Value = new decimal(new int[] { 10, 0, 0, 0 });
            nudEraserSize.ValueChanged += nudEraserSize_ValueChanged;
            // 
            lblOpacity.AutoSize = true;
            lblOpacity.BackColor = Color.White;
            lblOpacity.ForeColor = Color.FromArgb(71, 85, 105);
            lblOpacity.Location = new Point(952, 18);
            lblOpacity.Name = "lblOpacity";
            lblOpacity.Size = new Size(95, 18);
            lblOpacity.TabIndex = 110;
            lblOpacity.Text = "Độ mờ: 100%";
            // 
            trackBarOpacity.BackColor = Color.White;
            trackBarOpacity.Location = new Point(862, 46);
            trackBarOpacity.Maximum = 255;
            trackBarOpacity.Name = "trackBarOpacity";
            trackBarOpacity.Size = new Size(192, 32);
            trackBarOpacity.TabIndex = 109;
            trackBarOpacity.TickStyle = TickStyle.None;
            trackBarOpacity.Value = 255;
            trackBarOpacity.ValueChanged += trackBarOpacity_ValueChanged;
            // 
            // Group 5: WORKSPACE (X: 1070..1188)
            // 
            btnLayers.Location = new Point(1078, 12);
            btnLayers.Name = "btnLayers";
            btnLayers.Size = new Size(102, 66);
            btnLayers.TabIndex = 111;
            btnLayers.Text = "Layers";
            btnLayers.Click += btnLayers_Click;
            // 
            // Form1
            // 
            BackColor = Color.FromArgb(241, 245, 249);
            ClientSize = new Size(1220, 740);
            MinimumSize = new Size(1024, 600);
            Controls.Add(pictureBox1);
            Controls.Add(panelTools);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular);
            Name = "Form1";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "PaintLike Studio — Trình Vẽ & Xử Lý Ảnh Chuyên Nghiệp";
            ((System.ComponentModel.ISupportInitialize)pictureBox1).EndInit();
            panelTools.ResumeLayout(false);
            panelTools.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)nudEraserSize).EndInit();
            ((System.ComponentModel.ISupportInitialize)trackBarOpacity).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Panel panelTools;
        private System.Windows.Forms.Button btnLine;
        private System.Windows.Forms.Button btnRect;
        private System.Windows.Forms.Button btnCircle;
        private System.Windows.Forms.Button btnTriangle;
        private System.Windows.Forms.Button btnEraser;
        private System.Windows.Forms.Button btnColor;
        private System.Windows.Forms.Button btnStar;
        private System.Windows.Forms.NumericUpDown nudEraserSize;
        private System.Windows.Forms.ColorDialog colorDialog1;
        private Button btnTrapezoid;
        private System.Windows.Forms.Button btnImportImage;
        private System.Windows.Forms.Button btnLayers;
        private System.Windows.Forms.TrackBar trackBarOpacity;
        private System.Windows.Forms.Label lblOpacity;
        private Button btnRedo;
        private Button btnUndo;
        private Button btnSmartSelect;
        private Button BttHexagon;
        private Button BttDiamond;
        private Button BttArrows;
        private Button BttEllipse;
        private Button BttBezier;
        private Button BttRoundedRectangle;
        private Button btnExport;
        private Button btnSave;
    }
}
