// * * * * * * * * * * * * * * * * * * * * * *
// Author:     Lindsey Keene (NukeAndBeans)
// Contact:    Twitter @nukeandbeans, Discord Nuke#3681
//
// Description:
//
// * * * * * * * * * * * * * * * * * * * * * *


#if UNITY_EDITOR

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlpacaIT.DynamicLighting.Editor {

    [Overlay( typeof( SceneView ), "Dynamic Lights" )]
    public class DynamicLightingOverlay : Overlay {

        private DropdownField m_DropDown;

        private readonly List<string> m_Choices = new() {
            "512", "1024", "2048", "4096"
        };

        /// <inheritdoc />
        public override VisualElement CreatePanelContent() {
            VisualElement ve = new();

            m_DropDown = new DropdownField( m_Choices, "2048" );

            Button btn = new(
                () => {
                    Debug.Log( $"Value at IDX {m_DropDown.index} is {m_DropDown.value}" );
                }
            );

            btn.text = "Bake Scene Lighting";

            ve.Add( btn );
            ve.Add( new Label( "Lightmap Resolution" ) );

            ve.Add( m_DropDown );

            return ve;
        }
    }

}

#endif
