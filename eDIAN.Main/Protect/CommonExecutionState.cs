using Microsoft.InformationProtection;
using Microsoft.InformationProtection.Policy;
using Microsoft.InformationProtection.Policy.Actions;
using System;
using System.Collections.Generic;
using System.Linq;
using Label = Microsoft.InformationProtection.Label;

namespace eDIAN.Main.Protect
{
    /// <summary>
    /// 클래스 CommonExecutionState는 Microsoft Information Protection의 ExecutionState를 상속하여, 보호 작업을 수행하는 데 필요한 상태 정보를 캡슐화합니다. 
    /// 이 클래스는 새 레이블, 메타데이터, 콘텐츠 식별자, 할당 방법, 다운그레이드 정당성 및 지원되는 작업과 같은 다양한 속성을 포함하여 보호 작업의 실행 상태를 나타냅니다.
    /// IPolicyHandler 객체의 ComputeActions 메서드의 인수로 사용되어, 보호 작업을 수행하는 데 필요한 정보를 제공합니다.
    /// </summary>
    public class CommonExecutionState : Microsoft.InformationProtection.Policy.ExecutionState
    {
        public CommonExecutionState(Label newLabel) 
        {
            this.NewLabel = newLabel;
        }

        // Metadata
        public Dictionary<String, String> Metadata { get; set; }

        // New Label
        public Label NewLabel { get; set; }

        // Conent Identifier
        public String ContentIdentifier { get; set; }

        // Assignment Method
        public AssignmentMethod AssignmentMethod { get; set; } = AssignmentMethod.Standard;

        // Downgrade Justified
        public bool DowngradeJustified { get; set; }

        // Downgrade Justification
        public String DowngradeJustification { get; set; }

        // Template Id
        public String TemplateId { get; set; }

        /// <summary>
        /// Content Format
        /// Microsoft.InformationProtection.Policy.ContentFormat.File 
        /// Microsoft.InformationProtection.Policy.ContentFormat.Email
        /// </summary>
        public String ContentFormat { get; set; } = Microsoft.InformationProtection.Policy.ContentFormat.File;

        // Supported Actions
        public ActionType SupportedActions { get; set; } = ActionType.All;

        // Generate Audit Event 
        public bool GenerateAuditEvent { get; set; }

        /// <summary>
        /// Get Content Format
        /// </summary>
        /// <returns></returns>
        public override String GetContentFormat()
        {
            return ContentFormat;
        }

        /// <summary>
        /// Get Content Identifier
        /// </summary>
        /// <returns></returns>
        public override String GetContentIdentifier()
        {
            return ContentIdentifier;
        }

        /// <summary>
        /// Get Content Metadata
        /// </summary>
        /// <param name="names"></param>
        /// <param name="namePrefixes"></param>
        /// <returns></returns>
        public override List<MetadataEntry> GetContentMetadata(List<String> names, List<string> namePrefixes)
        {
            if (Metadata == null)
            {
                return new List<MetadataEntry>();
            }

            List<MetadataEntry> result = new List<MetadataEntry>();
            Dictionary<String, String> filteredMetadata = new Dictionary<String, String>();

            foreach (String namePrefix in namePrefixes)
            {
                foreach (KeyValuePair<String, String> metadata in Metadata.Where(r => r.Key.StartsWith(namePrefix)))
                {
                    filteredMetadata[metadata.Key] = metadata.Value;
                }
            }

            foreach (String name in names.Where(r => Metadata.ContainsKey(r)))
            {
                filteredMetadata[name] = Metadata[name];
            }

            foreach (KeyValuePair<String, String> metadata in filteredMetadata)
            {
                result.Add(new MetadataEntry(metadata.Key, metadata.Value, null));
            }

            if (filteredMetadata.Count > 0)
            {
                filteredMetadata.Clear();
            }

            return result;
        }

        /// <summary>
        /// Get New Label 
        /// </summary>
        /// <returns></returns>
        public override Label GetNewLabel()
        {
            return NewLabel;
        }

        /// <summary>
        /// Get New Label Assignment Method
        /// </summary>
        /// <returns></returns>
        public override AssignmentMethod GetNewLabelAssignmentMethod()
        {
            return AssignmentMethod;
        }

        /// <summary>
        /// Get Protection Descriptor
        /// </summary>
        /// <returns></returns>
        public override ProtectionDescriptor GetProtectionDescriptor()
        {
            if (string.IsNullOrEmpty(TemplateId))
            {
                return null;
            }

            return new ProtectionDescriptor(TemplateId);
        }

        /// <summary>
        /// Get Supported Actions
        /// </summary>
        /// <returns></returns>
        public override ActionType GetSupportedActions()
        {
            return SupportedActions;
        }

        /// <summary>
        /// Is Downgrade Justified
        /// </summary>
        /// <param name="justificationMessage"></param>
        /// <returns></returns>
        public override bool IsDowngradeJustified(out String justificationMessage)
        {
            justificationMessage = DowngradeJustification;
            return DowngradeJustified;
        }
    }
}
