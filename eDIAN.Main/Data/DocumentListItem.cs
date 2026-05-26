using eDIAN.Core;
using eDIAN.Data;
using eDIAN.Main.Core;
using eDIAN.Main.UI;
using log4net;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace eDIAN.Main.Data
{
    public class DocumentListItem
    {
        public String  id { get; set; }

        public BitmapImage IconFileType { get; set; }

        public String displayName { get; set; }

        public BitmapImage IconOwner { get; set; }

        public BitmapImage IconView { get; set; }

        public BitmapImage IconCopy { get; set; }

        public BitmapImage IconPrint { get; set; }

        public BitmapImage IconSave { get; set; }

        public String ToolTipOwner { get; set; }

        public String ToolTipView { get; set; }

        public String ToolTipCopy { get; set; }

        public String ToolTipPrint { get; set; }

        public String ToolTipSave { get; set; }

        public String buttonSensibilityVisible { get; set; }

        public ProtectedDocument source { get; set; }

        public DocumentListItem(String displayName, ProtectedDocument source)
        {
            this.id = DateTime.Now.ToString("yyyyMMddHHmmssfff");

            this.source = source;

            // 열린 문서 리스트 항목 폼에 표시
            this.bindDocumentItemUI(source, displayName);
        }

        public DocumentListItem(String id, String displayName, ProtectedDocument source)
        {
            this.id = id;

            this.source = source;

            // 열린 문서 리스트 항목 폼에 표시
            this.bindDocumentItemUI(source, displayName);
        }


        /// <summary>
        /// 열린 문서 리스트 항목 폼에 표시할 내용을 UI 에 바인딩
        /// </summary>
        /// <param name="source"></param>
        /// <param name="displayName"></param>
        private void bindDocumentItemUI(ProtectedDocument source, String displayName)
        {
            // 파일 유형 아이콘 표시

            this.IconFileType = PluginFormManager.ToBitmapImage(Properties.Resources.icon_file_protected);

            if (!this.source.isProtected)
            {
                if (this.source.isNewFile)
                {
                    this.IconFileType = PluginFormManager.ToBitmapImage(Properties.Resources.icon_file_new);
                }
                else
                {
                    this.IconFileType = PluginFormManager.ToBitmapImage(Properties.Resources.icon_file_unprotected);
                }
            }

            // 파일명 생성

            this.displayName = displayName + $"{(this.source.isUpdated ? " *" : "")} {(this.source.isReadOnly ? "(" + PluginApplication.global.getTitle("read.only")  + ")" : "")}";

            // 권한 아이콘 표시 (소유권, 보기, 복사, 인쇄, 저장 권한 여부에 따라 아이콘 표시)

            if (this.source.isProtected)
            {

                // 레이블 소유자, 권한 여부 아이콘 표시

                this.IconOwner = this.source.isOwner ? PluginFormManager.ToBitmapImage(Properties.Resources.icon_label_owner_on) : PluginFormManager.ToBitmapImage(Properties.Resources.icon_label_owner_off);
                this.IconView  = this.source.isView ? PluginFormManager.ToBitmapImage(Properties.Resources.icon_right_view_on) : PluginFormManager.ToBitmapImage(Properties.Resources.icon_right_view_off);
                this.IconCopy  = this.source.isExtract ? PluginFormManager.ToBitmapImage(Properties.Resources.icon_right_copy_on) : PluginFormManager.ToBitmapImage(Properties.Resources.icon_right_copy_off);
                this.IconPrint = this.source.isPrint ? PluginFormManager.ToBitmapImage(Properties.Resources.icon_right_print_on) : PluginFormManager.ToBitmapImage(Properties.Resources.icon_right_print_off);
                this.IconSave  = this.source.isEdit ? PluginFormManager.ToBitmapImage(Properties.Resources.icon_right_save_on) : PluginFormManager.ToBitmapImage(Properties.Resources.icon_right_save_off);

                // 레이블 소유자, 권한 여부 아이콘에 대한 툴팁 표시

                this.ToolTipOwner = this.source.isOwner ? PluginApplication.global.getTitle("label.owner") : PluginApplication.global.getTitle("label.user");  // "레이블 소유자" : "레이블 대상자"
                this.ToolTipView  = this.source.isView ? PluginApplication.global.getTitle("enable.view") : PluginApplication.global.getTitle("disable.view");  // "보기 가능" : "보기 불가";
                this.ToolTipCopy  = this.source.isExtract ? PluginApplication.global.getTitle("enable.copy") : PluginApplication.global.getTitle("disable.copy");  // "복사 가능" : "복사 불가";
                this.ToolTipPrint = this.source.isPrint ? PluginApplication.global.getTitle("enable.print") : PluginApplication.global.getTitle("disable.print");  // "인쇄 가능" : "인쇄 불가";
                this.ToolTipSave  = this.source.isEdit ? PluginApplication.global.getTitle("enable.save") : PluginApplication.global.getTitle("disable.save");  // "저장 가능" : "저장 불가";
            }
            else 
            {                 
                this.IconOwner = null;
                this.IconView  = null;
                this.IconCopy  = null;
                this.IconPrint = null;
                this.IconSave  = null;

                this.ToolTipOwner = "";
                this.ToolTipView  = "";
                this.ToolTipCopy  = "";
                this.ToolTipPrint = "";
                this.ToolTipSave  = "";
            }

            // 적용 버튼 보이기/숨김 제어

            String licenseType = PluginApplication.userLicenseData.licenseType ?? "";

            bool canApply = !this.source.isNewFile && !this.source.isReadOnly && licenseType.Equals("F");

            if (ServiceConstants.SERVICE_COMPANY.Equals("ZAISOFT", StringComparison.OrdinalIgnoreCase))
            {
                canApply = canApply && !this.source.isProtected;
            }

            if(canApply == true)
            {
                this.buttonSensibilityVisible = "Visible";
            }
            else
            {
                this.buttonSensibilityVisible = "hidden";
            }
        }
    }
}
