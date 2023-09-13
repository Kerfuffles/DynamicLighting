// * * * * * * * * * * * * * * * * * * * * * *
// Author:     Lindsey Keene (NukeAndBeans)
// Contact:    Twitter @nukeandbeans, Discord Nuke#3681
//
// Description:
//
// * * * * * * * * * * * * * * * * * * * * * *


using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;

namespace AlpacaIT.DynamicLighting.Editor {

    [Overlay( typeof( SceneView ), "Dynamic Lights" )]
    public class DynamicLightingOverlay : IMGUIOverlay {

        private bool isBaking           = false;
        private int  selectedResolution = 0;

        private readonly GUIContent[] resolutionChoices = {
            new( "512" ),
            new( "1024" ),
            new( "2048" ),
            new( "4096" )
        };

        private readonly GUILayoutOption[] m_Options = {
            GUILayout.Width( 200f )
        };

        private readonly GUIContent[] tooltipContents = {
            new( "Bake", "Starts a new lightmap bake." ),
            new( "Resolution", "Sets the maximum resolution to bake to." )
        };

        private EventHandler<EventArgs> OnLightmapperStateChange( object sender, bool baking ) {
            //Debug.Log( baking );

            isBaking = baking;

            return null;
        }

        /// <inheritdoc />
        public override void OnCreated() {
            base.OnCreated();

            DynamicLightManager.Instance.traceStarted -= OnLightmapperStateChange( null, true );
            DynamicLightManager.Instance.traceStarted += OnLightmapperStateChange( null, true );

            DynamicLightManager.Instance.traceCancelled -= OnLightmapperStateChange( null, false );
            DynamicLightManager.Instance.traceCancelled += OnLightmapperStateChange( null, false );

            DynamicLightManager.Instance.traceCompleted -= OnLightmapperStateChange( null, false );
            DynamicLightManager.Instance.traceCompleted += OnLightmapperStateChange( null, false );
        }

        private Rect lastRect;

        /// <inheritdoc />
        public override void OnGUI() {
            // doing this purely to ensure the entire ui is the appropriate width. this fixes an issue with IMGUI overlays causing weird size "flickering" that occurs otherwise
            using( EditorGUILayout.VerticalScope _ = new( m_Options ) ) {

                GUI.enabled = !isBaking;

                selectedResolution = EditorGUILayout.Popup( tooltipContents[1], selectedResolution, resolutionChoices, m_Options );

                if( GUILayout.Button( tooltipContents[0], m_Options ) ) {

                    switch( selectedResolution ) {
                        case 0: {
                            DynamicLightManager.Instance.Raytrace( 512 );

                            break;
                        }

                        case 1: {
                            DynamicLightManager.Instance.Raytrace( 1024 );

                            break;
                        }

                        case 2: {
                            DynamicLightManager.Instance.Raytrace( 2048 );

                            break;
                        }

                        case 3: {
                            DynamicLightManager.Instance.Raytrace( 4096 );

                            break;
                        }
                    }

                }
            }
        }
    }

}
